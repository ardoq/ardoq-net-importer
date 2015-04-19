using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Ardoq;
using Ardoq.Util;
using Ardoq.Models;
using Mono.Cecil;
using Mono.Cecil.Cil;

using System.Text;
using System.Reflection;
using System.IO;
using CommandLine;
using CommandLine.Text;
using System.Text.RegularExpressions;


namespace Ardoq
{
	public class MainClass
	{
		Workspace workspace;
		Model model;
		SyncRepository rep;

		[Option ('a', "AssemblyPath", Required = true,
			HelpText = "Input assembly to be documented.")]
		public String AssemblyPath { get; set; }

		[Option ('t', "token", Required = true, 
			HelpText = "Authentication token")]
		public String Token { get; set; }


		[Option ('m', "model", Required = false, DefaultValue = ".Net",
			HelpText = "Name of different model, must be based on .Net")]
		public String ModelName { get; set; }

		[Option ('o', "organization", Required = false, DefaultValue = "ardoq",
			HelpText = "Organization to store data in")]
		public String Org { get; set; }


		[Option ('h', "hostName", Required = false, DefaultValue = "https://app.ardoq.com",
			HelpText = "The Ardoq host")]
		public String HostName { get; set; }

		[Option ('r', "selfReference", Required = false, DefaultValue = false,
			HelpText = "Allow self references (not fully supported in Ardoq.)")]
		public bool SelfReference { get; set; }


		[Option ('s', "skipMethods", Required = false, DefaultValue = false,
			HelpText = "Don't add Method pages")]
		public bool SkipAddMethodToDocs { get; set; }

		[Option ('i', "opcodeInstruction", Required = false, DefaultValue = true,
			HelpText = "Analyse OpCode instructions in methods")]
		public bool AddInstructionReferences { get; set; }

		[Option ('e', "skipStoreExternalAssembly", Required = false, DefaultValue = false,
			HelpText = "Skip Store external assembly calls")]
		public bool SkipStoreExternalAssemblyDetail { get; set; }

		[Option ('d', "detail", Required = false, DefaultValue = false,
			HelpText = "Include private members")]
		public Boolean IncludePrivate { get; set; }

		[Option ('n', "notifyMe", Required = false, DefaultValue = false,
			HelpText = "Notify me by mail")]
		public Boolean NotifyByMail { get; set; }

		[Option ('f', "folderName", Required = false, DefaultValue = ".NET Assemblies",
			HelpText = "Folder name, set to blank to ignore")]
		public String FolderName { get; set; }

		Dictionary<string, Workspace> assemblyWorkspaceMap = new Dictionary<string, Workspace> ();

		Regex fixName = new Regex ("<{0,1}(.*?)>{0,1}");
		Regex fixRegexDeclaration = new Regex ("(.*?)<{0,1}(.*?)>{0,1}.*");

		String folderId = null;

		List<string> views = new List<string> () {
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

		[HelpOption]
		public string GetUsage ()
		{
			var help = new HelpText {
				Heading = new HeadingInfo ("Ardoq Assembly Doc", Assembly.GetExecutingAssembly ().GetName ().Version.ToString ()),
				Copyright = new CopyrightInfo ("Ardoq AS", 2015),
				AdditionalNewLineAfterOption = true,
				AddDashesToOption = true
			};
			help.AddOptions (this);
			return help;
		}

		public static void Main (string[] args)
		{
			var app = new MainClass (); 

			var result = Parser.Default.ParseArguments (args, app);
			Task<Workspace> scanTask;
			if (result) {

				scanTask = app.Run ();

				scanTask.Wait ();

			} else {
				if (app.Token == null) {
					Console.WriteLine ("Supply authentication token: -t <token>");
				}
				if (app.AssemblyPath == null) {
					Console.WriteLine ("Add assembly filename: -a <path/assembly.dll>");
				}
			}

		}

		public async Task<Workspace> Run ()
		{
			Console.WriteLine ("Connecting to: " + HostName);
			Console.WriteLine (" - : " + Org);
			var client = new ArdoqClient (new HttpClient (), HostName, Token, Org);
			rep = new SyncRepository (client);
		    model = null;
			try 
            {
				model = await client.ModelService.GetModelByName (".Net");
			} 
            catch (InvalidOperationException)
            {
                // Default model will be created
			}

            if (model == null)
                model = await CreateDefaultModel(client);

		    folderId = null;
			if (FolderName != null && FolderName.Length > 1) 
            {
				try 
                {
					var folder = await client.FolderService.GetFolderByName (FolderName);
					folderId = folder.Id;
				} catch (InvalidOperationException) {
                    // Folder will be created not by name
				}

			    if (folderId == null)
			    {
                    var folder = await client.FolderService.CreateFolder(FolderName);
                    folderId = folder.Id;
                }
            }
			var module = ModuleDefinition.ReadModule (AssemblyPath);

			String wsName = this.getAssemblyWorkspaceName (module.Assembly.Name);
			workspace = await rep.PrefetchWorkspace (wsName, model.Id);

			workspace.Views = views;
			workspace.Folder = folderId;

			var currentAssembly = await getNamespaceComp (module.Assembly.Name.Name, workspace);

			if (!SkipStoreExternalAssemblyDetail) {
				foreach (var c in module.AssemblyReferences) {
					Console.WriteLine ("Adding Assembly " + c.Name);
					var ws = await getWorkspace (c);
					var nsc = await getNamespaceComp (c.Name, ws);
					AddReference (currentAssembly, nsc, "", model.GetReferenceTypeByName ("Uses"));
				}
			}

			foreach (var type in module.Types)
			{
			    await ProcessModuleType(type);
			}

			await rep.Save ();
			await rep.CleanUpMissingComps ();
			Console.WriteLine (rep.GetReport ());


			if (NotifyByMail) {
				try {
					await client.NotifyService.PostMessage (new Message (".NET assembly is imported", "Your workspace " + workspace.Name + " is ready: " + HostName + "/app/view/workspace/" + workspace.Id + "?org=" + client.Org));	
				} catch (Exception) {
					Console.WriteLine ("Could not notify by mail.");
				}
			}

			Console.WriteLine ("Your workspace '" + workspace.Name + "' is ready: " + HostName + "/app/view/workspace/" + workspace.Id);

			//var ws = await client.WorkspaceService.GetWorkspaceById (workspace.Id);
			return workspace;

		}

	    private async Task ProcessModuleType(TypeDefinition type)
	    {
            if ((!type.IsPublic && IncludePrivate == false) || type.Name == "<Module>")
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
                await ProcessTypeInterfaces(type, typeComp);
            }

            if (type.BaseType != null && !type.BaseType.Namespace.StartsWith("System"))
            {
                await ProcessSystemBaseType(type, typeComp);
            }

            if (type.HasProperties)
            {
                await ProcessTypeProperties(type, typeComp);
            }

            if (type.HasFields)
            {
                await ProcessTypeFields(type, typeComp);
            }

            if (type.HasMethods)
            {
                await ProcessTypeMethods(type, typeComp);
            }

            typeComp.Description = MakeMarkup(typeComp.Description);
        }

	    private async Task<Model> CreateDefaultModel(ArdoqClient client)
        {
            var myAssembly = Assembly.GetExecutingAssembly();
            var resourceName = "ardoq-cecil-inspection.Resource.NetModel.json";

            using (var stream = myAssembly.GetManifestResourceStream(resourceName))
            using (var reader = new StreamReader(stream))
            {
                Console.WriteLine("Missing model, creating default model.");
                string result = reader.ReadToEnd();
                var model = await client.ModelService.UploadModel(result);
                await client.FieldService.CreateField(new Field("objectType", "Object type", model.Id, new List<String>() {
						model.GetComponentTypeByName ("Namespace"),
						model.GetComponentTypeByName ("Object"),
						model.GetComponentTypeByName ("Method")
					}, FieldType.Text));

                var fields = new Field[]
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
                    await client.FieldService.CreateField(field);
                }

                return model;
            }
        }

	    private async Task ProcessTypeInterfaces(TypeDefinition type, Component typeComp)
	    {
            foreach (var iface in type.Interfaces)
            {
                if (!iface.Namespace.StartsWith("System"))
                {
                    var interfaceComp = await getTypeReferenceComp(iface);
                    if (interfaceComp != null)
                        AddReference(typeComp, interfaceComp, "", model.GetReferenceTypeByName("Implements"));
                }
            }
        }

        private async Task ProcessSystemBaseType(TypeDefinition type, Component typeComp)
        {
            var interfaceComp = await getTypeReferenceComp(type.BaseType);
            if (interfaceComp != null)
                AddReference(typeComp, interfaceComp, "", model.GetReferenceTypeByName("Extends"));
        }

        private async Task ProcessTypeProperties(TypeDefinition type, Component typeComp)
        {
            foreach (var f in type.Properties)
            {
                if (f.PropertyType != null)
                {
                    if (!f.PropertyType.Namespace.StartsWith("System"))
                    {
                        var fieldComp = await getTypeReferenceComp(f.PropertyType);
                        if (fieldComp != null)
                            AddReference(typeComp, fieldComp, "", model.GetReferenceTypeByName("Uses"));
                    }
                    else
                    {
                        //System dep, let's just add a ref to namespace.
                        var fieldComp = rep.GetComp(f.PropertyType.Scope.Name);
                        if (fieldComp != null)
                        {
                            AddReference(typeComp, fieldComp, "", model.GetReferenceTypeByName("Uses"));
                        }
                    }
                }
            }
        }

        private async Task ProcessTypeFields(TypeDefinition type, Component typeComp)
        {
            foreach (var df in type.Fields)
            {
                if (df.FieldType != null)
                {
                    if (!df.FieldType.Namespace.StartsWith("System"))
                    {
                        var fieldComp = await getTypeReferenceComp(df.FieldType);
                        if (fieldComp != null)
                            AddReference(typeComp, fieldComp, "", model.GetReferenceTypeByName("Uses"));
                    }
                    else
                    {
                        //System dep, let's just add a ref to namespace.
                        var fieldComp = await getNamespaceComp(df.FieldType);
                        if (fieldComp != null)
                        {
                            AddReference(typeComp, fieldComp, "", model.GetReferenceTypeByName("Uses"));
                        }
                    }
                }
            }
        }

        private async Task ProcessTypeMethods(TypeDefinition type, Component typeComp)
        {
            var constructors = new StringBuilder();
            var methods = new StringBuilder();

            foreach (var method in type.Methods)
            {
                var methodComp = (!method.IsConstructor && (method.IsPublic || IncludePrivate))
                    ? await addMethodComp(method, typeComp)
                    : typeComp;

                await ProcessTypeMethod(type, typeComp, method, methodComp, constructors, methods);
            }

            typeComp.Description += "###Constructors\n" + constructors.ToString() + "###Methods\n" + methods.ToString();
        }

	    private async Task ProcessTypeMethod(TypeDefinition type, Component typeComp, MethodDefinition method, Component methodComp, 
            StringBuilder constructors, StringBuilder methods)
	    {
            var methodInfo = new StringBuilder();

	        await ProcessMethodHeader(type, method, methodInfo);

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
                await ProcessMethodBody(type, typeComp, method, methodComp, methodInfo);
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

        private async Task ProcessMethodHeader(TypeDefinition type, MethodDefinition method, StringBuilder methodInfo)
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

        private async Task ProcessMethodBody(TypeDefinition type, Component typeComp, 
            MethodDefinition method, Component methodComp, StringBuilder methodInfo)
        {
            if (method.Body.HasVariables)
            {
                await ProcessBodyVariables(typeComp, method);
            }

            if (AddInstructionReferences)
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
                            AddReference(typeComp, fieldComp, "", model.GetReferenceTypeByName("Uses"));
                    }
                    else
                    {
                        //System dep, let's just add a ref to namespace.
                        var fieldComp = rep.GetComp(f.VariableType.Scope.Name);
                        if (fieldComp != null)
                        {
                            AddReference(typeComp, fieldComp, "", model.GetReferenceTypeByName("Uses"));
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

	    public async Task<Workspace> getWorkspace (AssemblyNameReference c)
		{
			var name = getAssemblyWorkspaceName (c);
			if (!assemblyWorkspaceMap.ContainsKey (name)) {
				var ws = await rep.GetOrCreateWorkspace (name, model.Id, folderId, views);
				if (!assemblyWorkspaceMap.ContainsKey (name)) {
					assemblyWorkspaceMap.Add (name, ws);
				}

			}
			return assemblyWorkspaceMap [name];
		}

		public String getAssemblyWorkspaceName (AssemblyNameReference anr)
		{
			return anr.Name + " " + anr.Version;
		}

		public async Task<bool> addOpCodeRef (object o, Component mcomp, int order)
		{

			if (o is MethodDefinition) {
				MethodDefinition md = o as MethodDefinition;
				if (md.IsConstructor) {
					return false;
				}
			}

			if (o is MethodReference || o is MethodDefinition) {
				var md = o as MethodReference;
				//Don't want constructor "method" calls.
				if (!md.DeclaringType.IsGenericInstance && !md.IsGenericInstance && md.Name != ".ctor") {
					var declaringComp = await this.getTypeReferenceComp (md.DeclaringType);
					if (declaringComp != null) {
						var methodRef = await addMethodComp (md, declaringComp);
						if (methodRef != null) {
							var reference = AddReference
								(mcomp, methodRef, "Method call", model.GetReferenceTypeByName ("Calls"));
							if (reference == null) {
								return true;
							}
							string description = "";
							if (md.HasParameters) {
								foreach (var param in md.Parameters) {
									if (description.Length > 0) {
										description += ", ";

									}
									description += param.ParameterType.FullName + " " + param.Name;
								}
							}


							reference.Description = MakeMarkup (description);
							reference.Order = order;
							if (md.ReturnType != null && md.ReturnType.Name != "Void") {
								reference.ReturnValue = MakeMarkup (md.ReturnType.Name);
							}
						}
					}
				}

			} else {

				Console.WriteLine ("Unknown type: " + o.GetType ());
			}
			return true;
		}

		public  string MakeMarkup (String value)
		{
			if (!String.IsNullOrEmpty (value)) {
				return value.Replace ("<", "&lt;").Replace (">", "&gt;").Replace ("`", "\\`");
			}
			return "";
		}

		Regex GetterAndSetterFix = new Regex (".*? (.*)\\(.+");
		Regex GetterAndSetterRemoveVerb = new Regex (".et_");

		public  async Task<Component> addMethodComp (MethodReference mr, Component typeComp)
		{
			if (SkipAddMethodToDocs) {
				return typeComp;
			}
			var fullName = mr.FullName;
			var shortName = mr.Name;

			var isProperty = false;
			if (mr is MethodDefinition) {
				var md = mr as MethodDefinition;
				if (md.IsGetter || md.IsSetter) {
					fullName = GetterAndSetterRemoveVerb.Replace (GetterAndSetterFix.Replace (fullName, "$1"), "");
					shortName = GetterAndSetterRemoveVerb.Replace (shortName, "");
					isProperty = true;
				}
			}

			var methodComp = await rep.AddComp (fixMethodPathName (fullName), new Component (fixCompName (shortName), typeComp.RootWorkspace, "", model.GetComponentTypeByName ("Method")), typeComp);
			if (isProperty) {
				addField (methodComp, "icon", "table");
				addField (methodComp, "objectType", "Property");
			}
			return methodComp;
		}


		public  async Task<bool> addParameterRef (ParameterDefinition at, Component typeComp)
		{
			var componentTarget = await getParameterComp (at);
			if (componentTarget != null) {
				AddReference (typeComp, componentTarget, "Parameter reference.", model.GetReferenceTypeByName ("Uses"));
			}
			return true;
		}

		public  async Task<Component> getParameterComp (ParameterDefinition at)
		{
			if (!at.ParameterType.Namespace.StartsWith ("System") && !at.ParameterType.IsGenericInstance && !at.ParameterType.IsGenericParameter) {
				var componentTarget = await getTypeReferenceComp (at.ParameterType);
				componentTarget = (componentTarget != null) ? componentTarget : rep.GetComp (at.ParameterType.Namespace);
				componentTarget = (componentTarget != null) ? componentTarget : rep.GetComp (at.ParameterType.Scope.Name);
				return componentTarget;
			}
			return null;
		}

		public  void addAttributeRef (CustomAttribute at, Component typeComp)
		{
			var componentTarget = rep.GetComp (at.AttributeType.FullName);
			componentTarget = (componentTarget != null) ? componentTarget : rep.GetComp (at.AttributeType.Namespace);
			componentTarget = (componentTarget != null) ? componentTarget : rep.GetComp (at.AttributeType.Scope.Name);
			if (componentTarget != null) {
				AddReference (typeComp, componentTarget, "Attribute reference.", model.GetReferenceTypeByName ("Uses"));
			}
		}

		public  async void addRestService (CustomAttribute at, Component typeComp)
		{
			if (at.AttributeType.FullName.StartsWith ("Refit.")) {
				var service = MakeMarkup (at.ConstructorArguments [0].Value.ToString ());
				var operation = MakeMarkup (at.AttributeType.Name.Replace ("Attribute", "").ToLower ());
				var parentComp = await rep.AddComp (service, new Component (service, workspace.Id, "", model.GetComponentTypeByName ("Service")));
				var comp = await rep.AddComp (service + operation, new Component (operation, workspace.Id, "", model.GetComponentTypeByName ("Operation")), parentComp);
				AddReference (typeComp, comp, operation, model.GetReferenceTypeByName ("Uses"));

				Console.WriteLine (operation + " " + service);
			}
		}

		public  async Task<Component> getTypeReferenceComp (TypeReference tr)
		{
			var td = tr;
			if (td.IsArray) {
				td = td.GetElementType ();
			}

			if (td.IsByReference || td.Name == ".ctor" || td.Name.Contains ("<>") || td.Name.Contains ("PrivateImplementationDetails") || td.Name == "T" +
			    "" || td.IsGenericParameter || td.IsGenericInstance) {

				return null;

			}
			
			var ws = await getWorkspaceComp (td);
			if (ws == null) {
				return null;
			}

			var namespaceComp = await getNamespaceComp (td, ws);
			if (namespaceComp == null) {

				Console.WriteLine ("Warning: Unknown namespace for component: " + td.FullName);
				return null;
			}
			var compName = fixCompName ((td.IsNested) ? td.DeclaringType.Name : td.Name);
			var comp = await rep.AddComp (getFullPathName (td), new Component (compName, ws.Id, "", model.GetComponentTypeByName ("Object")), namespaceComp);
			if (td is TypeDefinition) {
				var tdNew = td as TypeDefinition;
				addField (comp, "objectType", GetObjectType (tdNew));
				addField (comp, "nrOfMethods", tdNew.Methods.Count);
				addField (comp, "nrOfFields", tdNew.Fields.Count);
				addField (comp, "classSize", tdNew.ClassSize);
				addField (comp, "nrOfCustomAttributes", tdNew.CustomAttributes.Count);
				addField (comp, "nrOfEvents", tdNew.Events.Count);
				addField (comp, "nrOfInterfaces", tdNew.Interfaces.Count);
				addField (comp, "icon", getIcon (tdNew));
			}

			addField (comp, "nrOfGenericParameters", td.GenericParameters.Count);

			return comp;
		}

		public object getIcon (TypeDefinition tdNew)
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

		public object GetObjectType (TypeDefinition tdNew)
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

		public void addField (Component comp, string fieldName, object fieldValue)
		{
			if (comp.Fields.ContainsKey (fieldName)) {
				comp.Fields [fieldName] = fieldValue;
			} else {
				comp.Fields.Add (fieldName, fieldValue);
			}
		}

		public async Task<Workspace> getWorkspaceComp (TypeReference td)
		{
			var ws = workspace;
			if (td.Scope is AssemblyNameReference && td.Scope.Name != td.Module.Assembly.Name.Name) {
				if (SkipStoreExternalAssemblyDetail) {
					return null;
				}
				ws = await getWorkspace (td.Scope as AssemblyNameReference);
			}
			return ws;
		}

		public async  Task<Component> getNamespaceComp (TypeReference td)
		{
			var ws = await getWorkspaceComp (td);
			if (ws == null) {
				return null;
			}
			return await getNamespaceComp (td, ws);
		}

		public async  Task<Component> getNamespaceComp (TypeReference td, Workspace ws)
		{
			var parentName = (td.IsNested) ? td.DeclaringType.Namespace : td.Namespace;
			return await getNamespaceComp (parentName, ws);
		}

		public async  Task<Component> getNamespaceComp (String parentName, Workspace ws)
		{
			var nsComp = (!String.IsNullOrEmpty (parentName)) ? await rep.AddComp (fixFullPathName (ws.Name + "/" + parentName), new Component (fixCompName (parentName), ws.Id, "", model.GetComponentTypeByName ("Namespace"))) : null;
			return nsComp;
		}

		Reference AddReference (Component source, Component target, string description, int type)
		{
			if (source == target && !SelfReference) {
				if (!source.Description.Contains ("#selfReference")) {
					source.Description += "\n #selfReference\n";
				}
				return null;
			}
			return rep.AddReference (source, target, description, type);
		}

		public String getFullPathName (TypeReference td)
		{
			var cFullName = (td.IsGenericInstance || td.IsGenericParameter) ? td.GetElementType ().FullName : td.FullName;
			return fixFullPathName (cFullName);
		}

		public String fixCompName (String name)
		{
			var newName = MakeMarkup (RemoveTypeDif.Replace (fixName.Replace (name, "$1"), ""));
			return newName;
		}

		Regex RemoveTypeDif = new Regex ("`\\d+");


		public String fixMethodPathName (string fullName)
		{
			var newName = RemoveTypeDif.Replace (fullName, "").Replace ("<", "").Replace (">", "");
			newName = MakeMarkup (newName);
			return newName;
		}

		public String fixFullPathName (String fullName)
		{
			var newName = RemoveTypeDif.Replace ((fullName.Contains ("<") ? fixRegexDeclaration.Replace (fullName, "$1$2") : fullName), "");
			newName = MakeMarkup (newName);
			return newName;
		}
			

	}
}
