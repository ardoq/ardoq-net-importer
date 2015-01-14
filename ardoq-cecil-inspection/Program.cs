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
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

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

		[Option ('i', "opcodeInstruction", Required = false, DefaultValue = true,
			HelpText = "Analyse OpCode instructions in methods")]
		public bool AddInstructionReferences { get; set; }

		[Option ('e', "storeExternalAssembly", Required = false, DefaultValue = true,
			HelpText = "Store external assembly calls")]
		public bool StoreExternalAssemblyDetail { get; set; }

		[Option ('d', "detail", Required = false, DefaultValue = false,
			HelpText = "Include private members")]
		public Boolean IncludePrivate { get; set; }

		Dictionary<string, Workspace> assemblyWorkspaceMap = new Dictionary<string, Workspace> ();

		Regex fixName = new Regex ("<{0,1}(.*?)>{0,1}");
		Regex fixRegexDeclaration = new Regex ("(.*?)<{0,1}(.*?)>{0,1}.*");

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
			try {
				model = await client.ModelService.GetModelByName (".Net");
			} catch (InvalidOperationException) {
				var myAssembly = Assembly.GetExecutingAssembly ();
				var resourceName = "ardoq-cecil-inspection.Resource..NetModel.json";

				using (Stream stream = myAssembly.GetManifestResourceStream (resourceName))
				using (StreamReader reader = new StreamReader (stream)) {
					Console.WriteLine ("Missing model, creating default model.");
					string result = reader.ReadToEnd ();
					model = await client.ModelService.UploadModel (result);
					await client.FieldService.CreateField (new Field ("objectType", "Object type", model.Id, new List<String> () {
						model.GetComponentTypeByName ("Namespace"),
						model.GetComponentTypeByName ("Object"),
						model.GetComponentTypeByName ("Method")
					}, FieldType.Text));
					await client.FieldService.CreateField (new Field ("nrOfMethods", "Nr of Methods", model.Id, new List<String> (){ model.GetComponentTypeByName ("Object") }, FieldType.Number));
					await client.FieldService.CreateField (new Field ("nrOfFields", "Nr of Fields", model.Id, new List<String> (){ model.GetComponentTypeByName ("Object") }, FieldType.Number));
					await client.FieldService.CreateField (new Field ("nrOfEvents", "Nr of Events", model.Id, new List<String> (){ model.GetComponentTypeByName ("Object") }, FieldType.Number));
					await client.FieldService.CreateField (new Field ("nrOfInterfaces", "Nr of Interfaces", model.Id, new List<String> (){ model.GetComponentTypeByName ("Object") }, FieldType.Number));
					await client.FieldService.CreateField (new Field ("nrOfCustomAttributes", "Nr of Custom Attributes", model.Id, new List<String> (){ model.GetComponentTypeByName ("Object") }, FieldType.Number));
					await client.FieldService.CreateField (new Field ("_fullName", "Path", model.Id, new List<String> () {
						model.GetComponentTypeByName ("Namespace"),
						model.GetComponentTypeByName ("Object"),
						model.GetComponentTypeByName ("Method")
					}, FieldType.Text));
				}


			}

			var module = ModuleDefinition.ReadModule (AssemblyPath);
			var ad = module.Assembly;


			String wsName = this.getAssemblyWorkspaceName (module.Assembly.Name);
			workspace = await rep.PrefetchWorkspace (wsName, model.Id);

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
			workspace.Views = views;

			var currentAssembly = await getNamespaceComp (module.Assembly.Name.Name, workspace);
			foreach (var c in module.AssemblyReferences) {
				Console.WriteLine ("Adding Assembly " + c.Name);
				var ws = await getWorkspace (c);
				var nsc = await getNamespaceComp (c.Name, ws);
				rep.AddReference (currentAssembly, nsc, "", model.GetReferenceTypeByName ("Uses"));
			}


			foreach (TypeDefinition type in module.Types) {

				if ((!type.IsPublic && IncludePrivate == false) || type.Name == "<Module>")
					continue;


				var typeComp = await getTypeReferenceComp (type);
				if (typeComp == null) {
					continue;
				}
				typeComp.Description = "";
				Console.WriteLine (type.FullName);
				if (type.HasInterfaces) {
					foreach (var iface in type.Interfaces) {
						if (!iface.Namespace.StartsWith ("System")) {
							var interfaceComp = await getTypeReferenceComp (iface);
							if (interfaceComp != null)
								rep.AddReference (typeComp, interfaceComp, "", model.GetReferenceTypeByName ("Implements"));
						}
					}

				}
				if (type.BaseType != null && !type.BaseType.Namespace.StartsWith ("System")) {
					var interfaceComp = await getTypeReferenceComp (type.BaseType);
					if (interfaceComp != null)
						rep.AddReference (typeComp, interfaceComp, "", model.GetReferenceTypeByName ("Extends"));
				}
				if (type.HasProperties) {
					foreach (var f in type.Properties) {
						if (f.PropertyType != null) {
							if (!f.PropertyType.Namespace.StartsWith ("System")) {
								var fieldComp = await getTypeReferenceComp (f.PropertyType);
								if (fieldComp != null)
									rep.AddReference (typeComp, fieldComp, "", model.GetReferenceTypeByName ("Uses"));
							} else {
								//System dep, let's just add a ref to namespace.
								var fieldComp = rep.GetComp (f.PropertyType.Scope.Name);
								if (fieldComp != null) {
									rep.AddReference (typeComp, fieldComp, "", model.GetReferenceTypeByName ("Uses"));
								}
							}
						}
					}
				}
				if (type.HasFields) {
					foreach (var df in type.Fields) {
						if (df.FieldType != null) {
							if (!df.FieldType.Namespace.StartsWith ("System")) {
								var fieldComp = await getTypeReferenceComp (df.FieldType);
								if (fieldComp != null)
									rep.AddReference (typeComp, fieldComp, "", model.GetReferenceTypeByName ("Uses"));
							} else {
								//System dep, let's just add a ref to namespace.
								var fieldComp = await getNamespaceComp (df.FieldType);
								if (fieldComp != null) {
									rep.AddReference (typeComp, fieldComp, "", model.GetReferenceTypeByName ("Uses"));
								}
							}
						}
					}
				}
				if (type.HasMethods) {
					StringBuilder constructors = new StringBuilder ();
					StringBuilder methods = new StringBuilder ();
					Component methodComp = typeComp;
					foreach (var method in type.Methods) {
						StringBuilder methodInfo = new StringBuilder ();
						methodInfo.Append ("####");
						if (method.IsConstructor) {
							methodInfo.Append ("new ");
							methodInfo.Append (type.Name);
						} else {
							methodInfo.Append (method.ReturnType.Name);
							methodInfo.Append (" ");
							methodInfo.Append (method.Name);
						}
						if (!method.IsConstructor && (method.IsPublic || IncludePrivate)) {
							methodComp = await addMethodComp (method, typeComp);
						} else {
							methodComp = typeComp;

						}
						methodInfo.Append ("(");
						if (method.HasParameters) {
							foreach (var p in method.Parameters) {
								methodInfo.Append (p.ParameterType.FullName);
								methodInfo.Append (" ");
								methodInfo.Append (p.Name);
								methodInfo.Append (", ");
								await addParameterRef (p, methodComp);
							}
							methodInfo.Remove (methodInfo.Length - 2, 1);
						}
						methodInfo.AppendLine (")");


						//Console.WriteLine ("-" + method.Name);
						if (method.HasCustomAttributes) {
							foreach (var at in method.CustomAttributes) {
								/*if (!at.AttributeType.Name.Equals ("CompilerGeneratedAttribute") && !at.AttributeType.FullName.StartsWith ("System")) {
									addRestService (at, methodComp);
									addAttributeRef (at, methodComp);
									//Console.WriteLine ("==" + at.AttributeType.FullName + " - " + at.AttributeType.GenericParameters);
								}*/
							}
						}
						if (method.HasBody) {
							if (method.Body.HasVariables) {
								foreach (var f in method.Body.Variables) {
									if (f.VariableType != null && f.VariableType.DeclaringType != null) {
										if (!f.VariableType.Namespace.StartsWith ("System")) {
											var fieldComp = await getTypeReferenceComp (f.VariableType.DeclaringType);
											rep.AddReference (typeComp, fieldComp, "", model.GetReferenceTypeByName ("Uses"));
										} else {
											//System dep, let's just add a ref to namespace.
											var fieldComp = rep.GetComp (f.VariableType.Scope.Name);
											if (fieldComp != null) {
												rep.AddReference (typeComp, fieldComp, "", model.GetReferenceTypeByName ("Uses"));
											}
										}
									}
								}
							}
							if (AddInstructionReferences) {
								foreach (var inst in method.Body.Instructions) {
									if (inst.OpCode.Name.IndexOf ("ldloca.s") > -1 && inst.Operand is VariableDefinition) {
										var mbr = inst.Operand as VariableDefinition;
										if (mbr.VariableType is TypeDefinition) {
											var vttd = mbr.VariableType as TypeDefinition;
											foreach (var opm in vttd.Methods) {
												if (opm.HasBody) {
													foreach (var newInst in opm.Body.Instructions) {
														if (newInst.OpCode.Name.IndexOf ("call") > -1) {
															await addOpCodeRef (newInst.Operand, methodComp, newInst.Offset);
														}
													}
												}

											}
										}

									} else if (inst.OpCode.Name.IndexOf ("call") > -1) {
										await addOpCodeRef (inst.Operand, methodComp, inst.Offset);
									}

								}
							}
						}

						if (method.IsConstructor) {
							constructors.AppendLine (methodInfo.ToString ()); 
						} else {
							methods.AppendLine (methodInfo.ToString ()); 
						}

					}

					typeComp.Description += "###Constructors\n" + constructors.ToString () + "###Methods\n" + methods.ToString ();

				}

				typeComp.Description = MakeMarkup (typeComp.Description);
			}

			await rep.Save ();
			await rep.CleanUpMissingComps ();
			Console.WriteLine (rep.GetReport ());


			Console.WriteLine ("Your workspace '" + workspace.Name + "' is ready: " + HostName + "/app/view/workspace/" + workspace.Id);
			//var ws = await client.WorkspaceService.GetWorkspaceById (workspace.Id);
			return workspace;

		}

		async Task<Workspace> getWorkspace (AssemblyNameReference c)
		{
			var name = getAssemblyWorkspaceName (c);
			if (!assemblyWorkspaceMap.ContainsKey (name)) {
				var ws = await rep.GetOrCreateWorkspace (name, model.Id);
				if (!assemblyWorkspaceMap.ContainsKey (name)) {
					assemblyWorkspaceMap.Add (name, ws);
				}

			}
			return assemblyWorkspaceMap [name];
		}


		String getAssemblyWorkspaceName (AssemblyNameReference anr)
		{
			return anr.Name + " " + anr.Version;
		}

		async Task<bool> addOpCodeRef (object o, Component mcomp, int order)
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
							var reference = rep.AddReference
								(mcomp, methodRef, "Method call", model.GetReferenceTypeByName ("Calls"));
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

		private string MakeMarkup (String value)
		{
			if (!String.IsNullOrEmpty (value)) {
				return value.Replace ("<", "&lt;").Replace (">", "&gt;").Replace ("`", "\\`");
			}
			return "";
		}

		Regex GetterAndSetterFix = new Regex (".*? (.*)\\(.+");
		Regex GetterAndSetterRemoveVerb = new Regex (".et_");

		private async Task<Component> addMethodComp (MethodReference mr, Component typeComp)
		{
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


		private async Task<bool> addParameterRef (ParameterDefinition at, Component typeComp)
		{
			var componentTarget = await getParameterComp (at);
			if (componentTarget != null) {
				rep.AddReference (typeComp, componentTarget, "Parameter reference.", model.GetReferenceTypeByName ("Uses"));
			}
			return true;
		}

		private async Task<Component> getParameterComp (ParameterDefinition at)
		{
			if (!at.ParameterType.Namespace.StartsWith ("System") && !at.ParameterType.IsGenericInstance && !at.ParameterType.IsGenericParameter) {
				var componentTarget = await getTypeReferenceComp (at.ParameterType);
				componentTarget = (componentTarget != null) ? componentTarget : rep.GetComp (at.ParameterType.Namespace);
				componentTarget = (componentTarget != null) ? componentTarget : rep.GetComp (at.ParameterType.Scope.Name);
				return componentTarget;
			}
			return null;
		}

		private void addAttributeRef (CustomAttribute at, Component typeComp)
		{
			var componentTarget = rep.GetComp (at.AttributeType.FullName);
			componentTarget = (componentTarget != null) ? componentTarget : rep.GetComp (at.AttributeType.Namespace);
			componentTarget = (componentTarget != null) ? componentTarget : rep.GetComp (at.AttributeType.Scope.Name);
			if (componentTarget != null) {
				rep.AddReference (typeComp, componentTarget, "Attribute reference.", model.GetReferenceTypeByName ("Uses"));
			}
		}

		private async void addRestService (CustomAttribute at, Component typeComp)
		{
			if (at.AttributeType.FullName.StartsWith ("Refit.")) {
				var service = MakeMarkup (at.ConstructorArguments [0].Value.ToString ());
				var operation = MakeMarkup (at.AttributeType.Name.Replace ("Attribute", "").ToLower ());
				var parentComp = await rep.AddComp (service, new Component (service, workspace.Id, "", model.GetComponentTypeByName ("Service")));
				var comp = await rep.AddComp (service + operation, new Component (operation, workspace.Id, "", model.GetComponentTypeByName ("Operation")), parentComp);
				rep.AddReference (typeComp, comp, operation, model.GetReferenceTypeByName ("Uses"));

				Console.WriteLine (operation + " " + service);
			}
		}

		private async Task<Component> getTypeReferenceComp (TypeReference tr)
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

		object getIcon (TypeDefinition tdNew)
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

		object GetObjectType (TypeDefinition tdNew)
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

		void addField (Component comp, string fieldName, object fieldValue)
		{
			if (comp.Fields.ContainsKey (fieldName)) {
				comp.Fields [fieldName] = fieldValue;
			} else {
				comp.Fields.Add (fieldName, fieldValue);
			}
		}

		private async Task<Workspace> getWorkspaceComp (TypeReference td)
		{
			var ws = workspace;
			if (td.Scope is AssemblyNameReference && td.Scope.Name != td.Module.Assembly.Name.Name) {
				ws = await getWorkspace (td.Scope as AssemblyNameReference);
			}
			return ws;
		}

		private async  Task<Component> getNamespaceComp (TypeReference td)
		{
			var ws = await getWorkspaceComp (td);
			return await getNamespaceComp (td, ws);
		}

		private async  Task<Component> getNamespaceComp (TypeReference td, Workspace ws)
		{
			var parentName = (td.IsNested) ? td.DeclaringType.Namespace : td.Namespace;
			return await getNamespaceComp (parentName, ws);
		}

		private async  Task<Component> getNamespaceComp (String parentName, Workspace ws)
		{
			var nsComp = (!String.IsNullOrEmpty (parentName)) ? await rep.AddComp (fixFullPathName (ws.Name + "/" + parentName), new Component (fixCompName (parentName), ws.Id, "", model.GetComponentTypeByName ("Namespace"))) : null;
			return nsComp;
		}

		private String getFullPathName (TypeReference td)
		{
			var cFullName = (td.IsGenericInstance || td.IsGenericParameter) ? td.GetElementType ().FullName : td.FullName;
			return fixFullPathName (cFullName);
		}

		private String fixCompName (String name)
		{
			var newName = MakeMarkup (RemoveTypeDif.Replace (fixName.Replace (name, "$1"), ""));
			return newName;
		}

		Regex RemoveTypeDif = new Regex ("`\\d+");


		private String fixMethodPathName (string fullName)
		{
			var newName = RemoveTypeDif.Replace (fullName, "").Replace ("<", "").Replace (">", "");
			newName = MakeMarkup (newName);
			return newName;
		}

		private String fixFullPathName (String fullName)
		{
			var newName = RemoveTypeDif.Replace ((fullName.Contains ("<") ? fixRegexDeclaration.Replace (fullName, "$1$2") : fullName), "");
			newName = MakeMarkup (newName);
			return newName;
		}
			

	}
}
