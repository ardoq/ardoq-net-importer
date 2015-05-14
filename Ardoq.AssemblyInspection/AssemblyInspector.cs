using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Ardoq.Formatter;
using Ardoq.Models;
using Ardoq.Util;
using Mono.Cecil;

namespace Ardoq.AssemblyInspection
{
    public class AssemblyInspector
    {
        private readonly Dictionary<string, Workspace> assemblyWorkspaceMap = new Dictionary<string, Workspace>();
        private readonly ModuleDefinition module;
        private readonly XmlDocument xmlDocumentation;
        private readonly IModel model;
        private readonly SyncRepository rep;
        private readonly Workspace workspace;
        private readonly InspectionOptions options;

        private readonly Regex fixName = new Regex("<{0,1}(.*?)>{0,1}");
        private readonly Regex fixRegexDeclaration = new Regex("(.*?)<{0,1}(.*?)>{0,1}.*");
        internal readonly Regex RemoveTypeDif = new Regex("`\\d+");

        public AssemblyInspector(Workspace workspace, ModuleDefinition module, XmlDocument xmlDocumentation, 
            IModel model, SyncRepository rep, InspectionOptions options)
        {
            this.workspace = workspace;
            this.module = module;
            this.xmlDocumentation = xmlDocumentation;
            this.model = model;
            this.rep = rep;
            this.options = options;
        }

        public async Task<Workspace> getWorkspace(AssemblyNameReference c)
        {
            var name = getAssemblyWorkspaceName(c);
            if (!assemblyWorkspaceMap.ContainsKey(name))
            {
                var ws = await rep.GetOrCreateWorkspace(name, model.Id);
                if (!assemblyWorkspaceMap.ContainsKey(name))
                {
                    assemblyWorkspaceMap.Add(name, ws);
                }

            }
            return assemblyWorkspaceMap[name];
        }

        public async Task InspectModuleAssemblies()
        {
            var currentAssembly = await getNamespaceComp(module.Assembly.Name.Name, workspace);

            if (!options.SkipExternalAssemblyDetails)
            {
                foreach (var c in module.AssemblyReferences)
                {
                    Console.WriteLine("Adding Assembly " + c.Name);
                    var ws = await getWorkspace(c);
                    var nsc = await getNamespaceComp(c.Name, ws);
                    rep.AddReference(currentAssembly, nsc, "", model.GetReferenceTypeByName("Uses"));
                }
            }

            await InspectModuleTypes();
        }

        public async Task InspectModuleTypes()
        {
            foreach (var type in module.Types)
            {
                await new TypeInspector(this, workspace, xmlDocumentation, model, workspace.Id, type, rep, options)
                    .InspectModuleType();
            }
        }

        public string getAssemblyWorkspaceName(AssemblyNameReference anr)
        {
            return anr.Name + " " + anr.Version;
        }

        public async Task<Component> getNamespaceComp(string parentName, Workspace ws)
        {
            var nsComp = (!String.IsNullOrEmpty(parentName)) 
                ? await rep.AddComp(fixFullPathName(ws.Name + "/" + parentName),
                    new Component(fixCompName(parentName), ws.Id, "", model.GetComponentTypeByName("Namespace"))) 
                : null;
            return nsComp;
        }

        public string fixCompName(string name)
        {
            return FormatterUtils.MakeMarkup(
                RemoveTypeDif.Replace(fixName.Replace(name, "$1"), ""));
        }

        public string fixFullPathName(string fullName)
        {
            return FormatterUtils.MakeMarkup(
                RemoveTypeDif.Replace((fullName.Contains("<") 
                    ? fixRegexDeclaration.Replace(fullName, "$1$2") 
                    : fullName), ""));
        }

    }
}
