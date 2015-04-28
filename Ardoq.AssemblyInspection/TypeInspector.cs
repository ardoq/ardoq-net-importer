using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Ardoq.Fomatter;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Ardoq.Models;
using Ardoq.Util;

namespace Ardoq.AssemblyInspection
{
    public class TypeInspector
    {
        private readonly AssemblyInspector assemblyInspector;
        private readonly Workspace workspace;
        private readonly IModel model;
        private readonly string workspaceId;
        private TypeDefinition type;
        private readonly Formatter formatter;
        private readonly SyncRepository rep;
        private readonly InspectionOptions options;

        public TypeInspector(AssemblyInspector assemblyInspector, Workspace workspace, IModel model, string workspaceId, 
            TypeDefinition type, SyncRepository rep, InspectionOptions options)
        {
            this.assemblyInspector = assemblyInspector;
            this.workspace = workspace;
            this.model = model;
            this.workspaceId = workspaceId;
            this.type = type;
            this.rep = rep;
            this.options = options;
            this.formatter = new Formatter();
        }

        public async Task ProcessModuleType()
        {
            if ((!type.IsPublic && options.IncludePrivateMethods == false) || type.Name == "<Module>")
                return;

            var typeComp = await getTypeReferenceComp(type);
            if (typeComp == null)
            {
                return;
            }

            typeComp.Description = string.Empty;
            Console.WriteLine(type.FullName);

            if (type.HasInterfaces)
            {
                await ProcessTypeInterfaces(typeComp);
            }

            if (type.BaseType != null && !type.BaseType.Namespace.StartsWith("System"))
            {
                await ProcessSystemBaseType(typeComp);
            }

            if (type.HasProperties)
            {
                await ProcessTypeProperties(typeComp);
            }

            if (type.HasFields)
            {
                await ProcessTypeFields(typeComp);
            }

            if (type.HasMethods)
            {
                await ProcessTypeMethods(typeComp);
            }

            typeComp.Description = formatter.MakeMarkup(typeComp.Description);
        }

        private async Task ProcessTypeInterfaces(Component typeComp)
        {
            foreach (var iface in type.Interfaces)
            {
                if (!iface.Namespace.StartsWith("System"))
                {
                    var interfaceComp = await getTypeReferenceComp(iface);
                    if (interfaceComp != null)
                        rep.AddReference(typeComp, interfaceComp, "", model.GetReferenceTypeByName("Implements"));
                }
            }
        }

        private async Task ProcessSystemBaseType(Component typeComp)
        {
            var interfaceComp = await getTypeReferenceComp(type.BaseType);
            if (interfaceComp != null)
                rep.AddReference(typeComp, interfaceComp, "", model.GetReferenceTypeByName("Extends"));
        }

        private async Task ProcessTypeProperties(Component typeComp)
        {
            foreach (var f in type.Properties)
            {
                if (f.PropertyType != null)
                {
                    if (!f.PropertyType.Namespace.StartsWith("System"))
                    {
                        var fieldComp = await getTypeReferenceComp(f.PropertyType);
                        if (fieldComp != null)
                            rep.AddReference(typeComp, fieldComp, "", model.GetReferenceTypeByName("Uses"));
                    }
                    else
                    {
                        //System dep, let's just add a ref to namespace.
                        var fieldComp = rep.GetComp(f.PropertyType.Scope.Name);
                        if (fieldComp != null)
                        {
                            rep.AddReference(typeComp, fieldComp, "", model.GetReferenceTypeByName("Uses"));
                        }
                    }
                }
            }
        }

        private async Task ProcessTypeFields(Component typeComp)
        {
            foreach (var df in type.Fields)
            {
                if (df.FieldType != null)
                {
                    if (!df.FieldType.Namespace.StartsWith("System"))
                    {
                        var fieldComp = await getTypeReferenceComp(df.FieldType);
                        if (fieldComp != null)
                            rep.AddReference(typeComp, fieldComp, "", model.GetReferenceTypeByName("Uses"));
                    }
                    else
                    {
                        //System dep, let's just add a ref to namespace.
                        var fieldComp = await getNamespaceComp(df.FieldType);
                        if (fieldComp != null)
                        {
                            rep.AddReference(typeComp, fieldComp, "", model.GetReferenceTypeByName("Uses"));
                        }
                    }
                }
            }
        }

        private async Task ProcessTypeMethods(Component typeComp)
        {
            var constructors = new StringBuilder();
            var methods = new StringBuilder();

            foreach (var method in type.Methods)
            {
                var methodComp = (!method.IsConstructor && (method.IsPublic || options.IncludePrivateMethods))
                    ? await addMethodComp(method, typeComp)
                    : typeComp;

                await ProcessTypeMethod(typeComp, method, methodComp, constructors, methods);
            }

            typeComp.Description += "###Constructors\n" + constructors.ToString() + "###Methods\n" + methods.ToString();
        }

        private async Task ProcessTypeMethod(Component typeComp, MethodDefinition method, Component methodComp,
            StringBuilder constructors, StringBuilder methods)
        {
            var methodInfo = new StringBuilder();

            await ProcessMethodHeader(method, methodInfo);

            if (method.HasParameters)
            {
                await ProcessMethodParameters(method, methodComp, methodInfo);
            }
            methodInfo.AppendLine(")");

            //Console.WriteLine ("-" + method.Name);
            if (method.HasCustomAttributes)
            {
                await ProcessMethodCustomAttirbutes(method, methodComp);
            }

            if (method.HasBody)
            {
                await ProcessMethodBody(typeComp, method, methodComp, methodInfo);
            }

            if (method.IsConstructor)
            {
                constructors.AppendLine(methodInfo.ToString());
            }
            else
            {
                methods.AppendLine(methodInfo.ToString());
            }
        }

        private async Task ProcessMethodHeader(MethodDefinition method, StringBuilder methodInfo)
        {
            methodInfo.Append("####");
            if (method.IsConstructor)
            {
                methodInfo.Append("new ");
                methodInfo.Append(type.Name);
            }
            else
            {
                methodInfo.Append(method.ReturnType.Name);
                methodInfo.Append(" ");
                methodInfo.Append(method.Name);
            }
            methodInfo.Append("(");
        }

        private async Task ProcessMethodParameters(MethodDefinition method, Component methodComp,
            StringBuilder methodInfo)
        {
            foreach (var p in method.Parameters)
            {
                methodInfo.Append(p.ParameterType.FullName);
                methodInfo.Append(" ");
                methodInfo.Append(p.Name);
                methodInfo.Append(", ");
                await addParameterRef(p, methodComp);
            }
            methodInfo.Remove(methodInfo.Length - 2, 1);
        }

        private async Task ProcessMethodCustomAttirbutes(MethodDefinition method, Component methodComp)
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

        private async Task ProcessMethodBody(Component typeComp,
            MethodDefinition method, Component methodComp, StringBuilder methodInfo)
        {
            if (method.Body.HasVariables)
            {
                await ProcessBodyVariables(typeComp, method);
            }

            if (options.IncludeInstructionReferences)
            {
                await ProcessBodyInstructions(method, methodComp);
            }
        }

        private async Task ProcessBodyVariables(Component typeComp, MethodDefinition method)
        {
            foreach (var f in method.Body.Variables)
            {
                if (f.VariableType != null && f.VariableType.DeclaringType != null)
                {
                    if (!f.VariableType.Namespace.StartsWith("System"))
                    {
                        var fieldComp = await getTypeReferenceComp(f.VariableType.DeclaringType);
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

        private async Task ProcessBodyInstructions(MethodDefinition method, Component methodComp)
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

        public async Task<bool> addOpCodeRef(object o, Component mcomp, int order)
        {

            if (o is MethodDefinition)
            {
                MethodDefinition md = o as MethodDefinition;
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
                    var declaringComp = await this.getTypeReferenceComp(md.DeclaringType);
                    if (declaringComp != null)
                    {
                        var methodRef = await addMethodComp(md, declaringComp);
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

                            reference.Description = formatter.MakeMarkup(description);
                            reference.Order = order;
                            if (md.ReturnType != null && md.ReturnType.Name != "Void")
                            {
                                reference.ReturnValue = formatter.MakeMarkup(md.ReturnType.Name);
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

        public async Task<bool> addParameterRef(ParameterDefinition at, Component typeComp)
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
                var componentTarget = await getTypeReferenceComp(at.ParameterType);
                componentTarget = componentTarget ?? rep.GetComp(at.ParameterType.Namespace);
                componentTarget = componentTarget ?? rep.GetComp(at.ParameterType.Scope.Name);
                return componentTarget;
            }
            return null;
        }

        public void addAttributeRef(CustomAttribute at, Component typeComp)
        {
            var componentTarget = rep.GetComp(at.AttributeType.FullName);
            componentTarget = componentTarget ?? rep.GetComp(at.AttributeType.Namespace);
            componentTarget = componentTarget ?? rep.GetComp(at.AttributeType.Scope.Name);
            if (componentTarget != null)
            {
                rep.AddReference(typeComp, componentTarget, "Attribute reference.", model.GetReferenceTypeByName("Uses"));
            }
        }

        public async void addRestService(CustomAttribute at, Component typeComp)
        {
            if (at.AttributeType.FullName.StartsWith("Refit."))
            {
                var service = formatter.MakeMarkup(at.ConstructorArguments[0].Value.ToString());
                var operation = formatter.MakeMarkup(at.AttributeType.Name.Replace("Attribute", "").ToLower());
                var parentComp = await rep.AddComp(service, new Component(service, workspaceId, "", model.GetComponentTypeByName("Service")));
                var comp = await rep.AddComp(service + operation, new Component(operation, workspaceId, "", model.GetComponentTypeByName("Operation")), parentComp);
                rep.AddReference(typeComp, comp, operation, model.GetReferenceTypeByName("Uses"));

                Console.WriteLine(operation + " " + service);
            }
        }

        public async Task<Component> addMethodComp(MethodReference mr, Component typeComp)
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

            var methodComp = await rep.AddComp(assemblyInspector.fixMethodPathName(fullName), 
                new Component(assemblyInspector.fixCompName(shortName), typeComp.RootWorkspace, "", model.GetComponentTypeByName("Method")), typeComp);
            if (isProperty)
            {
                addField(methodComp, "icon", "table");
                addField(methodComp, "objectType", "Property");
            }
            return methodComp;
        }

        public async Task<Component> getTypeReferenceComp(TypeReference tr)
        {
            var td = tr;
            if (td.IsArray)
            {
                td = td.GetElementType();
            }

            if (td.IsByReference || td.Name == ".ctor" || td.Name.Contains("<>") || td.Name.Contains("PrivateImplementationDetails") || td.Name == "T" +
                "" || td.IsGenericParameter || td.IsGenericInstance)
            {

                return null;

            }

            var ws = await getWorkspaceComp(td);
            if (ws == null)
            {
                return null;
            }

            var namespaceComp = await getNamespaceComp(td, ws);
            if (namespaceComp == null)
            {

                Console.WriteLine("Warning: Unknown namespace for component: " + td.FullName);
                return null;
            }
            var compName = assemblyInspector.fixCompName((td.IsNested) ? td.DeclaringType.Name : td.Name);
            var comp = await rep.AddComp(getFullPathName(td), new Component(compName, ws.Id, "", model.GetComponentTypeByName("Object")), namespaceComp);
            if (td is TypeDefinition)
            {
                var tdNew = td as TypeDefinition;
                addField(comp, "objectType", GetObjectType(tdNew));
                addField(comp, "nrOfMethods", tdNew.Methods.Count);
                addField(comp, "nrOfFields", tdNew.Fields.Count);
                addField(comp, "classSize", tdNew.ClassSize);
                addField(comp, "nrOfCustomAttributes", tdNew.CustomAttributes.Count);
                addField(comp, "nrOfEvents", tdNew.Events.Count);
                addField(comp, "nrOfInterfaces", tdNew.Interfaces.Count);
                addField(comp, "icon", getIcon(tdNew));
            }

            addField(comp, "nrOfGenericParameters", td.GenericParameters.Count);

            return comp;
        }

        public object getIcon(TypeDefinition tdNew)
        {
            var type = "building";
            if (tdNew.IsEnum)
                return "bar-chart";
            if (tdNew.IsInterface)
                return "exchange";

            if (tdNew.IsAbstract)
                return "beaker";
            return type;
        }

        public object GetObjectType(TypeDefinition tdNew)
        {
            var type = "Class";
            if (tdNew.IsEnum)
                return "Enum";
            if (tdNew.IsInterface)
                return "Interface";

            if (tdNew.IsAbstract)
                return "Abstract Class";
            return type;
        }

        public void addField(Component comp, string fieldName, object fieldValue)
        {
            if (comp.Fields.ContainsKey(fieldName))
            {
                comp.Fields[fieldName] = fieldValue;
            }
            else
            {
                comp.Fields.Add(fieldName, fieldValue);
            }
        }

        public async Task<Workspace> getWorkspaceComp(TypeReference td)
        {
            var ws = workspace;
            if (td.Scope is AssemblyNameReference && td.Scope.Name != td.Module.Assembly.Name.Name)
            {
                if (options.SkipExternalAssemblyDetails)
                {
                    return null;
                }
                ws = await assemblyInspector.getWorkspace(td.Scope as AssemblyNameReference);
            }
            return ws;
        }

        public async Task<Component> getNamespaceComp(TypeReference td)
        {
            var ws = await getWorkspaceComp(td);
            if (ws == null)
            {
                return null;
            }
            return await getNamespaceComp(td, ws);
        }

        public async Task<Component> getNamespaceComp(TypeReference td, Workspace ws)
        {
            var parentName = (td.IsNested) ? td.DeclaringType.Namespace : td.Namespace;
            return await assemblyInspector.getNamespaceComp(parentName, ws);
        }

        public String getFullPathName(TypeReference td)
        {
            var cFullName = (td.IsGenericInstance || td.IsGenericParameter) ? td.GetElementType().FullName : td.FullName;
            return assemblyInspector.fixFullPathName(cFullName);
        }

        Regex GetterAndSetterFix = new Regex(".*? (.*)\\(.+");
        Regex GetterAndSetterRemoveVerb = new Regex(".et_");
    }
}
