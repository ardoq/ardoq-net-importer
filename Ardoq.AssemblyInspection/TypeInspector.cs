using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardoq.Formatter;
using Mono.Cecil;
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
        }

        public async Task InspectModuleType()
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
                await InspectTypeInterfaces(typeComp);
            }

            if (type.BaseType != null && !type.BaseType.Namespace.StartsWith("System"))
            {
                await InspectSystemBaseType(typeComp);
            }

            if (type.HasProperties)
            {
                await InspectTypeProperties(typeComp);
            }

            if (type.HasFields)
            {
                await InspectTypeFields(typeComp);
            }

            if (type.HasMethods)
            {
                await InspectTypeMethods(typeComp);
            }

            typeComp.Description = FormatterUtils.MakeMarkup(typeComp.Description);
        }

        private async Task InspectTypeInterfaces(Component typeComp)
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

        private async Task InspectSystemBaseType(Component typeComp)
        {
            var interfaceComp = await getTypeReferenceComp(type.BaseType);
            if (interfaceComp != null)
                rep.AddReference(typeComp, interfaceComp, "", model.GetReferenceTypeByName("Extends"));
        }

        internal async Task InspectTypeProperties(Component typeComp)
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

        private async Task InspectTypeFields(Component typeComp)
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

        private async Task<string> InspectTypeMethods(Component typeComp)
        {
            var typeFormatter = new TypeFormatter();
            foreach (var method in type.Methods)
            {
                var methodInfo = await new MethodInspector(assemblyInspector, this, workspace, model, workspace.Id, 
                    type, typeComp, method, rep, options)
                    .InspectTypeMethod();

                if (method.IsConstructor)
                {
                    typeFormatter.WriteConstructorInfo(methodInfo);
                }
                else
                {
                    typeFormatter.WriteMethodInfo(methodInfo);
                }
            }

            typeComp.Description += typeFormatter.GetTypeInfo();
            return typeFormatter.GetTypeInfo();
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
    }
}
