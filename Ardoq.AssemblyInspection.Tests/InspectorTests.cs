using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Mono.Cecil;
using NUnit.Framework;
using Moq;
using Ardoq.Models;
using Ardoq.Service.Interface;
using Ardoq.Util;

namespace Ardoq.AssemblyInspection.Tests
{
    [TestFixture]
    public class InspectorTests
    {
		Mock<Workspace> fakeWorkspace;
		Mock<IModel> fakeModel;
		Mock<SyncRepository> fakeRep;

		public InspectorTests()
		{
            fakeWorkspace = new Mock<Workspace>("testWorkspace", "testComponentModel", "testDescription");
			fakeModel = new Mock<IModel> ();

            var fakeWorkspaceService = new Mock<IWorkspaceService>();
            var workspaces = new List<Workspace> { fakeWorkspace.Object };
            fakeWorkspaceService.Setup(x => x.GetAllWorkspaces(It.IsAny<string>())).ReturnsAsync(workspaces);
            fakeWorkspaceService.Setup(x => x.CreateWorkspace(It.IsAny<Workspace>(), It.IsAny<string>()))
                .ReturnsAsync(fakeWorkspace.Object);

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

			fakeRep = new Mock<SyncRepository>(fakeClient.Object);
		}

        [Test]
        public async Task TypeTest()
        {
			var module = ModuleDefinition.ReadModule(this.CallingAssemblyPath);
            var options = new InspectionOptions();
            await fakeRep.Object.PrefetchWorkspace(string.Format("{0} {1}",
                Assembly.GetCallingAssembly().GetName().Name, Assembly.GetCallingAssembly().GetName().Version), 
                fakeModel.Object.Id);
			var assemblyInspector = new AssemblyInspector(fakeWorkspace.Object, module, fakeModel.Object, fakeRep.Object, options);
			await assemblyInspector.InspectModuleAssemblies (fakeWorkspace.Object);

            foreach (var type in module.Types)
            {
                await new TypeInspector(assemblyInspector, fakeWorkspace.Object, fakeModel.Object, fakeWorkspace.Object.Id, type, fakeRep.Object, options)
                    .ProcessModuleType();
            }
        }

		public string CallingAssemblyPath
		{
			get
			{
				string codeBase = Assembly.GetCallingAssembly().CodeBase;
				UriBuilder uri = new UriBuilder(codeBase);
				return Uri.UnescapeDataString(uri.Path);
			}
		}
    }
}
