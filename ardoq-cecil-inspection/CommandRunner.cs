using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Ardoq.AssemblyInspection;
using Ardoq.Models;
using Ardoq.Service.Interface;
using Ardoq.Util;
using Mono.Cecil;
using Moq;

namespace Ardoq
{
    public class CommandRunner
    {
        private Workspace workspace;
        private IModel model;
        private SyncRepository rep;

        string folderId = null;

        List<string> views = new List<string>() {
			"swimlane",
			"sequence",
			"reader",
			"tagscape",
			"tableview",
			"treemap",
			"componenttree",
			"relationships",
			"integrations",
			"processflow"
		};

        public async Task<Workspace> Run(CommandOptions command)
        {
            IArdoqClient client;
            if (command.Token == "local")
            {
                client = await SetupFakeClient();
            }
            else
            {
                client = await SetupArdoqClient(command);
            }

            rep = new SyncRepository(client);
            var module = ModuleDefinition.ReadModule(command.AssemblyPath);
            var assemblyName = module.Assembly.Name;
            workspace = await rep.PrefetchWorkspace(string.Format("{0} {1}",
                assemblyName.Name, assemblyName.Version), model.Id);
            workspace.Views = views;
            workspace.Folder = folderId;

            var options = new InspectionOptions()
            {
                SelfReference = command.SelfReference,
                IncludeInstructionReferences = command.AddInstructionReferences,
                IncludePrivateMethods = command.IncludePrivate,
                SkipAddMethodsToDocs = command.SkipAddMethodToDocs,
                SkipExternalAssemblyDetails = command.SkipStoreExternalAssemblyDetail,
            };

            var assemblyInspector = new AssemblyInspector(workspace, module, model, rep, options);
            await assemblyInspector.InspectModuleAssemblies(workspace);

            foreach (var type in module.Types)
            {
                await new TypeInspector(assemblyInspector, workspace, model, workspace.Id, type, rep, options)
                    .ProcessModuleType();
            }

            await rep.Save();
            await rep.CleanUpMissingComps();
            Console.WriteLine(rep.GetReport());

            if (command.NotifyByMail)
            {
                try
                {
                    await client.NotifyService.PostMessage(new Message(".NET assembly is imported", "Your workspace " + workspace.Name + " is ready: " + command.HostName + "/app/view/workspace/" + workspace.Id + "?org=" + client.Org));
                }
                catch (Exception)
                {
                    Console.WriteLine("Could not notify by mail.");
                }
            }

            Console.WriteLine("Your workspace '" + workspace.Name + "' is ready: " + command.HostName + "/app/view/workspace/" + workspace.Id);

            //var ws = await client.WorkspaceService.GetWorkspaceById (workspace.Id);
            return workspace;
        }

        private Task<IArdoqClient> SetupFakeClient()
        {
            Console.WriteLine("Faking Ardoq client");

            var fakeWorkspace = new Workspace("testWorkspace", "testComponentModel", "testDescription");

            var fakeWorkspaceService = new Mock<IWorkspaceService>();
            var workspaces = new List<Workspace> { fakeWorkspace };
            fakeWorkspaceService.Setup(x => x.GetAllWorkspaces(It.IsAny<string>())).ReturnsAsync(workspaces);
            fakeWorkspaceService.Setup(x => x.CreateWorkspace(It.IsAny<Workspace>(), It.IsAny<string>()))
                .ReturnsAsync(fakeWorkspace);

            var fakeComponent = new Component();

            var fakeComponentService = new Mock<IComponentService>();
            fakeComponentService.Setup(x => x.CreateComponent(It.IsAny<Component>(), It.IsAny<string>()))
                .ReturnsAsync(fakeComponent);

            var fakeReferenceService = new Mock<IReferenceService>();

            var fakeClient = new Mock<IArdoqClient>();
            fakeClient.SetupAllProperties();
            fakeClient.SetupGet(x => x.WorkspaceService).Returns(fakeWorkspaceService.Object);
            fakeClient.SetupGet(x => x.ComponentService).Returns(fakeComponentService.Object);
            fakeClient.SetupGet(x => x.ReferenceService).Returns(fakeReferenceService.Object);

            var fakeModel = new Mock<IModel>();
            fakeModel.SetupAllProperties();
            model = fakeModel.Object;
            folderId = "FakeFolderId";

            return Task.FromResult(fakeClient.Object);
        }

        private async Task<IArdoqClient> SetupArdoqClient(CommandOptions command)
        {
            Console.WriteLine("Connecting to: " + command.HostName);
            Console.WriteLine(" - : " + command.Org);

            var client = new ArdoqClient(new HttpClient(), command.HostName, command.Token, command.Org);
            model = null;
            try
            {
                model = await client.ModelService.GetModelByName(".Net", client.Org);
            }
            catch (InvalidOperationException)
            {
                // Default model will be created
            }

            if (model == null)
                model = await CreateDefaultModel(client);

            folderId = null;
            if (command.FolderName != null && command.FolderName.Length > 1)
            {
                try
                {
                    var folder = await client.FolderService.GetFolderByName(command.FolderName);
                    folderId = folder.Id;
                }
                catch (InvalidOperationException)
                {
                    // Folder will be created not by name
                }

                if (folderId == null)
                {
                    var folder = await client.FolderService.CreateFolder(
                        new Folder(command.FolderName, ""), client.Org);
                    folderId = folder.Id;
                }
            }

            return client;
        }

        private async Task<IModel> CreateDefaultModel(IArdoqClient client)
        {
            var myAssembly = Assembly.GetExecutingAssembly();
            var resourceName = "ardoq-cecil-inspection.Resource.NetModel.json";

            using (var stream = myAssembly.GetManifestResourceStream(resourceName))
            using (var reader = new StreamReader(stream))
            {
                Console.WriteLine("Missing model, creating default model.");
                string result = reader.ReadToEnd();
                var model = await client.ModelService.UploadModel(result, client.Org);
                await
                    client.FieldService.CreateField(new Field("objectType", "Object type", model.Id, new List<String>()
                    {
                        model.GetComponentTypeByName("Namespace"),
                        model.GetComponentTypeByName("Object"),
                        model.GetComponentTypeByName("Method")
                    }, FieldType.Text), client.Org);

                var fields = new[]
                {
                    new Field("nrOfMethods", "Nr of Methods", model.Id,
                        new List<String>() {model.GetComponentTypeByName("Object")}, FieldType.Number),
                    new Field("nrOfFields", "Nr of Fields", model.Id,
                        new List<String>() {model.GetComponentTypeByName("Object")}, FieldType.Number),
                    new Field("nrOfEvents", "Nr of Events", model.Id,
                        new List<String>() {model.GetComponentTypeByName("Object")}, FieldType.Number),
                    new Field("nrOfInterfaces", "Nr of Interfaces", model.Id,
                        new List<String>() {model.GetComponentTypeByName("Object")}, FieldType.Number),
                    new Field("nrOfCustomAttributes", "Nr of Custom Attributes", model.Id,
                        new List<String>() {model.GetComponentTypeByName("Object")}, FieldType.Number),
                    new Field("_fullName", "Path", model.Id, new List<String>()
                    {
                        model.GetComponentTypeByName("Namespace"),
                        model.GetComponentTypeByName("Object"),
                        model.GetComponentTypeByName("Method")
                    }, FieldType.Text),
                };

                foreach (var field in fields)
                {
                    await client.FieldService.CreateField(field, client.Org);
                }

                return model;
            }
        }
    }
}