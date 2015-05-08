using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Ardoq.Formatter;
using Ardoq.Models;
using Ardoq.Util;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Ardoq.AssemblyInspection
{
    public class MethodInspector
    {
        private readonly AssemblyInspector assemblyInspector;
        private readonly TypeInspector typeInspector;
        private readonly Workspace workspace;
        private readonly IModel model;
        private readonly string workspaceId;
        private TypeDefinition type;
        private Component typeComp;
        private MethodDefinition method;
        private readonly SyncRepository rep;
        private readonly InspectionOptions options;
        private Component methodComp;
        private MethodFormatter methodFormatter;

        private readonly Regex GetterAndSetterFix = new Regex(".*? (.*)\\(.+");
        private readonly Regex GetterAndSetterRemoveVerb = new Regex(".et_");

        public MethodInspector(AssemblyInspector assemblyInspector, TypeInspector typeInspector, 
            Workspace workspace, IModel model, string workspaceId,
            TypeDefinition type, Component typeComp, MethodDefinition method, 
            SyncRepository rep, InspectionOptions options)
        {
            this.assemblyInspector = assemblyInspector;
            this.typeInspector = typeInspector;
            this.workspace = workspace;
            this.model = model;
            this.workspaceId = workspaceId;
            this.type = type;
            this.typeComp = typeComp;
            this.method = method;
            this.rep = rep;
            this.options = options;
            this.methodFormatter = new MethodFormatter();
        }

        public async Task<string> InspectTypeMethod()
        {
            methodComp = (!method.IsConstructor && (method.IsPublic || options.IncludePrivateMethods))
                ? await addMethodComp(method)
                : typeComp;

            methodFormatter.WriteMethodHeader(method.Name, type.Name, method.ReturnType.Name, method.IsConstructor, 
                method.Parameters.Select(x => new Tuple<string, string>(x.Name, x.ParameterType.FullName)));

            if (method.HasParameters)
            {
                await InspectMethodParameters();
            }

            //Console.WriteLine ("-" + method.Name);
            if (method.HasCustomAttributes)
            {
                await InspectMethodCustomAttirbutes();
            }

            if (method.HasBody)
            {
                await InspectMethodBody();
            }

            return methodFormatter.GetMethodInfo();
        }

        private async Task InspectMethodParameters()
        {
            foreach (var p in method.Parameters)
            {
                await addParameterRef(p);
            }
        }

        private async Task InspectMethodCustomAttirbutes()
        {
            foreach (var at in method.CustomAttributes)
            {
                /*if (!at.AttributeType.Name.Equals ("CompilerGeneratedAttribute") && !at.AttributeType.FullName.StartsWith ("System")) {
                    addRestService (at, methodComp);
                    addAttributeRef (at, methodComp);
                    //Console.WriteLine ("==" + at.AttributeType.FullName + " - " + at.AttributeType.GenericParameters);
                }*/
            }
        }

        private async Task InspectMethodBody()
        {
            if (method.Body.HasVariables)
            {
                await InspectBodyVariables();
            }

            if (options.IncludeInstructionReferences)
            {
                await InspectBodyInstructions();
            }
        }

        private async Task InspectBodyVariables()
        {
            foreach (var f in method.Body.Variables)
            {
                if (f.VariableType != null && f.VariableType.DeclaringType != null)
                {
                    if (!f.VariableType.Namespace.StartsWith("System"))
                    {
                        var fieldComp = await typeInspector.getTypeReferenceComp(f.VariableType.DeclaringType);
                        if (fieldComp != null)
                            rep.AddReference(typeComp, fieldComp, "", model.GetReferenceTypeByName("Uses"));
                    }
                    else
                    {
                        //System dep, let's just add a ref to namespace.
                        var fieldComp = rep.GetComp(f.VariableType.Scope.Name);
                        if (fieldComp != null)
                        {
                            rep.AddReference(typeComp, fieldComp, "", model.GetReferenceTypeByName("Uses"));
                        }
                    }
                }
            }
        }

        private async Task InspectBodyInstructions()
        {
            foreach (var inst in method.Body.Instructions)
            {
                if (inst.OpCode.Name.IndexOf("ldloca.s") > -1 && inst.Operand is VariableDefinition)
                {
                    var mbr = inst.Operand as VariableDefinition;
                    if (mbr.VariableType is TypeDefinition)
                    {
                        var vttd = mbr.VariableType as TypeDefinition;
                        foreach (var opm in vttd.Methods)
                        {
                            if (opm.HasBody)
                            {
                                foreach (var newInst in opm.Body.Instructions)
                                {
                                    if (newInst.OpCode.Name.IndexOf("call") > -1)
                                    {
                                        await addOpCodeRef(newInst.Operand, methodComp, newInst.Offset);
                                    }
                                }
                            }

                        }
                    }

                }
                else if (inst.OpCode.Name.IndexOf("call") > -1)
                {
                    await addOpCodeRef(inst.Operand, methodComp, inst.Offset);
                }

            }
        }

        public async Task<Component> addMethodComp(MethodReference mr)
        {
            if (options.SkipAddMethodsToDocs)
            {
                return typeComp;
            }
            var fullName = mr.FullName;
            var shortName = mr.Name;

            var isProperty = false;
            if (mr is MethodDefinition)
            {
                var md = mr as MethodDefinition;
                if (md.IsGetter || md.IsSetter)
                {
                    fullName = GetterAndSetterRemoveVerb.Replace(GetterAndSetterFix.Replace(fullName, "$1"), "");
                    shortName = GetterAndSetterRemoveVerb.Replace(shortName, "");
                    isProperty = true;
                }
            }

            var methodComp = await rep.AddComp(fixMethodPathName(fullName),
                new Component(assemblyInspector.fixCompName(shortName), typeComp.RootWorkspace, "", model.GetComponentTypeByName("Method")), typeComp);
            if (isProperty)
            {
                typeInspector.addField(methodComp, "icon", "table");
                typeInspector.addField(methodComp, "objectType", "Property");
            }
            return methodComp;
        }

        public async Task<bool> addOpCodeRef(object o, Component mcomp, int order)
        {

            if (o is MethodDefinition)
            {
                var md = o as MethodDefinition;
                if (md.IsConstructor)
                {
                    return false;
                }
            }

            if (o is MethodReference || o is MethodDefinition)
            {
                var md = o as MethodReference;
                //Don't want constructor "method" calls.
                if (!md.DeclaringType.IsGenericInstance && !md.IsGenericInstance && md.Name != ".ctor")
                {
                    var declaringComp = await typeInspector.getTypeReferenceComp(md.DeclaringType);
                    if (declaringComp != null)
                    {
                        var methodRef = await addMethodComp(md);
                        if (methodRef != null)
                        {
                            var reference = rep.AddReference
                                (mcomp, methodRef, "Method call", model.GetReferenceTypeByName("Calls"));
                            if (reference == null)
                            {
                                return true;
                            }
                            string description = "";
                            if (md.HasParameters)
                            {
                                foreach (var param in md.Parameters)
                                {
                                    if (description.Length > 0)
                                    {
                                        description += ", ";

                                    }
                                    description += param.ParameterType.FullName + " " + param.Name;
                                }
                            }

                            reference.Description = FormatterUtils.MakeMarkup(description);
                            reference.Order = order;
                            if (md.ReturnType != null && md.ReturnType.Name != "Void")
                            {
                                reference.ReturnValue = FormatterUtils.MakeMarkup(md.ReturnType.Name);
                            }
                        }
                    }
                }

            }
            else
            {
                Console.WriteLine("Unknown type: " + o.GetType());
            }
            return true;
        }

        public async Task<bool> addParameterRef(ParameterDefinition at)
        {
            var componentTarget = await getParameterComp(at);
            if (componentTarget != null)
            {
                rep.AddReference(typeComp, componentTarget, "Parameter reference.", model.GetReferenceTypeByName("Uses"));
            }
            return true;
        }

        public async Task<Component> getParameterComp(ParameterDefinition at)
        {
            if (!at.ParameterType.Namespace.StartsWith("System") && !at.ParameterType.IsGenericInstance && !at.ParameterType.IsGenericParameter)
            {
                var componentTarget = await typeInspector.getTypeReferenceComp(at.ParameterType);
                componentTarget = componentTarget ?? rep.GetComp(at.ParameterType.Namespace);
                componentTarget = componentTarget ?? rep.GetComp(at.ParameterType.Scope.Name);
                return componentTarget;
            }
            return null;
        }

        public void addAttributeRef(CustomAttribute at)
        {
            var componentTarget = rep.GetComp(at.AttributeType.FullName);
            componentTarget = componentTarget ?? rep.GetComp(at.AttributeType.Namespace);
            componentTarget = componentTarget ?? rep.GetComp(at.AttributeType.Scope.Name);
            if (componentTarget != null)
            {
                rep.AddReference(typeComp, componentTarget, "Attribute reference.", model.GetReferenceTypeByName("Uses"));
            }
        }

        public async void addRestService(CustomAttribute at)
        {
            if (at.AttributeType.FullName.StartsWith("Refit."))
            {
                var service = FormatterUtils.MakeMarkup(at.ConstructorArguments[0].Value.ToString());
                var operation = FormatterUtils.MakeMarkup(at.AttributeType.Name.Replace("Attribute", "").ToLower());
                var parentComp = await rep.AddComp(service, new Component(service, workspaceId, "", model.GetComponentTypeByName("Service")));
                var comp = await rep.AddComp(service + operation, new Component(operation, workspaceId, "", model.GetComponentTypeByName("Operation")), parentComp);
                rep.AddReference(typeComp, comp, operation, model.GetReferenceTypeByName("Uses"));

                Console.WriteLine(operation + " " + service);
            }
        }

        public string fixMethodPathName(string fullName)
        {
            return FormatterUtils.MakeMarkup(
                assemblyInspector.RemoveTypeDif.Replace(fullName, "").Replace("<", "").Replace(">", ""));
        }
    }
}
