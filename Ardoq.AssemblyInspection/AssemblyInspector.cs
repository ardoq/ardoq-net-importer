using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Ardoq.Fomatter;
using Ardoq.Models;
using Ardoq.Util;
using Mono.Cecil;

namespace Ardoq.AssemblyInspection
{
    public class AssemblyInspector
    {
        private readonly Dictionary<string, Workspace> assemblyWorkspaceMap = new Dictionary<string, Workspace>();
        private readonly ModuleDefinition module;
        private readonly IModel model;
        private readonly SyncRepository rep;
        private Workspace workspace;
        private readonly InspectionOptions options;

        public AssemblyInspector(Workspace workspace, ModuleDefinition module, IModel model, 
            SyncRepository rep, InspectionOptions options)
        {
            this.workspace = workspace;
            this.module = module;
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

        public async Task InspectModuleAssemblies(Workspace workspace)
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
        }

        public String getAssemblyWorkspaceName(AssemblyNameReference anr)
        {
            return anr.Name + " " + anr.Version;
        }

        Regex fixName = new Regex("<{0,1}(.*?)>{0,1}");
        Regex fixRegexDeclaration = new Regex("(.*?)<{0,1}(.*?)>{0,1}.*");
        Regex RemoveTypeDif = new Regex("`\\d+");

        public async Task<Component> getNamespaceComp(String parentName, Workspace ws)
        {
            var nsComp = (!String.IsNullOrEmpty(parentName)) ? await rep.AddComp(fixFullPathName(ws.Name + "/" + parentName),
                new Component(fixCompName(parentName), ws.Id, "", model.GetComponentTypeByName("Namespace"))) : null;
            return nsComp;
        }

        public String fixCompName(String name)
        {
            var newName = new Formatter().MakeMarkup(RemoveTypeDif.Replace(fixName.Replace(name, "$1"), ""));
            return newName;
        }

        public String fixMethodPathName(string fullName)
        {
            var newName = RemoveTypeDif.Replace(fullName, "").Replace("<", "").Replace(">", "");
            newName = new Formatter().MakeMarkup(newName);
            return newName;
        }

        public String fixFullPathName(String fullName)
        {
            var newName = RemoveTypeDif.Replace((fullName.Contains("<") ? fixRegexDeclaration.Replace(fullName, "$1$2") : fullName), "");
            newName = new Formatter().MakeMarkup(newName);
            return newName;
        }

    }
}
