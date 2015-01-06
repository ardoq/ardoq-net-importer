using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Ardoq;
using Ardoq.Util;
using Ardoq.Models;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

using System.Text;

namespace test
{
	class MainClass
	{
		Workspace workspace;
		Model model;
		SyncRepository rep;
		Component parentAssemply;

		bool AddInstructionReferences { get; set; }

		Dictionary<string, Workspace> assemblyWorkspaceMap = new Dictionary<string, Workspace> ();

		public static void Main (string[] args)
		{
			//var workspace = new MainClass ().Run ("Mono.Cecil.dll");
			var workspace = new MainClass ().Run ("Ardoq.dll");

			workspace.Wait ();
			Console.WriteLine (workspace.Result);
		}


		public async Task<Workspace> Run (String assembly)
		{
			AddInstructionReferences = false;
			var client = new ArdoqClient (new HttpClient (), "http://localhost:8080", "7e098874b0c844b095b06a1604af8217", "shared");
			List<Component> compFound = await client.ComponentService.FieldSearch ("Mono.Cecil.dll", "name", "set_Url");


			rep = new SyncRepository (client);
			model = await client.ModelService.GetModelByName (".NET");

			var module = ModuleDefinition.ReadModule (assembly);
			var ad = module.Assembly;

			String wsName = this.getAssemblyWorkspace (module.Assembly.Name);
			workspace = await rep.PrefetchWorkspace (module.Assembly.Name.Name + " " + module.Assembly.Name.Version, model.Id);

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
			//workspace = await client.WorkspaceService.CreateWorkspace (ws);
			//["swimlane", "sequence", "reader", "tagscape", "tableview", "treemap", "componenttree", "relationships", "integrations", "processflow"]
			parentAssemply = rep.AddComp (module.Name, getAssemblyComp (module.Assembly));

			foreach (var c in module.AssemblyReferences) {
				var ws = await rep.GetOrCreateWorkspace (getAssemblyWorkspace (c), model.Id);
				assemblyWorkspaceMap.Add (getAssemblyWorkspace (c), ws);
				
				//Console.WriteLine ("Adding Assembly " + c.Name);
				//var assemblyRef = rep.AddComp (c.Name + " " + c.Version, getAssemblyReference (c));
				//c.GetType ();
				//rep.AddReference (parentAssemply, assemblyRef, "", model.GetReferenceTypeByName ("Uses"));
			}


			foreach (TypeDefinition type in module.Types) {
				if (!type.IsPublic)
					continue;

				var typeComp = await getTypeReferenceComp (type, type.IsInterface ? "Interface" : "Class");
				typeComp.Description = "";
				Console.WriteLine (type.FullName);
				if (type.HasInterfaces) {
					foreach (var iface in type.Interfaces) {
						if (!iface.Namespace.StartsWith ("System")) {
							var interfaceComp = await getTypeReferenceComp (iface, "Interface");
							if (interfaceComp != null)
								rep.AddReference (typeComp, interfaceComp, "", model.GetReferenceTypeByName ("Implements"));
						}
					}

				}
				if (type.BaseType != null && !type.BaseType.Namespace.StartsWith ("System")) {
					var interfaceComp = await getTypeReferenceComp (type.BaseType, "Class");
					if (interfaceComp != null)
						rep.AddReference (typeComp, interfaceComp, "", model.GetReferenceTypeByName ("Extends"));
				}
				if (type.HasProperties) {
					foreach (var f in type.Properties) {
						if (f.PropertyType != null) {
							if (!f.PropertyType.Namespace.StartsWith ("System")) {
								var fieldComp = await getTypeReferenceComp (f.PropertyType, "Class");
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
					foreach (var f in type.Fields) {
						if (f.FieldType != null) {
							if (!f.FieldType.Namespace.StartsWith ("System")) {
								var fieldComp = await getTypeReferenceComp (f.FieldType, "Class");
								if (fieldComp != null)
									rep.AddReference (typeComp, fieldComp, "", model.GetReferenceTypeByName ("Uses"));
							} else {
								//System dep, let's just add a ref to namespace.
								var fieldComp = rep.GetComp (f.FieldType.Scope.Name);
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
						if (!type.IsInterface && !method.IsConstructor && method.IsPublic && !method.IsGetter) {
							methodComp = addMethodComp (method, typeComp);
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
								addParameterRef (p, methodComp);
							}
							methodInfo.Remove (methodInfo.Length - 2, 1);
						}
						methodInfo.AppendLine (")");


						//Console.WriteLine ("-" + method.Name);
						if (method.HasCustomAttributes) {
							foreach (var at in method.CustomAttributes) {
								if (!at.AttributeType.Name.Equals ("CompilerGeneratedAttribute") && !at.AttributeType.FullName.StartsWith ("System")) {
									addRestService (at, methodComp);
									addAttributeRef (at, methodComp);
									//Console.WriteLine ("==" + at.AttributeType.FullName + " - " + at.AttributeType.GenericParameters);
								}
							}
						}
						if (method.HasBody) {
							if (method.Body.HasVariables) {
								foreach (var f in method.Body.Variables) {
									if (f.VariableType != null && f.VariableType.DeclaringType != null) {
										if (!f.VariableType.Namespace.StartsWith ("System")) {
											var fieldComp = await getTypeReferenceComp (f.VariableType.DeclaringType, "Class");
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
									if (inst.OpCode.Name.IndexOf ("call") > -1) {
										addOpCodeRef (inst.Operand, methodComp);
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

				typeComp.Description = typeComp.Description.Replace ("<", "&lt;").Replace (">", "&gt;");
			}

			await rep.Save ();
			await rep.CleanUpMissingComps ();
			Console.WriteLine (rep.GetReport ());
			return await client.WorkspaceService.GetWorkspaceById (workspace.Id);
		}



		String getAssemblyWorkspace (AssemblyNameReference anr)
		{
			return anr.Name + " " + anr.Version;
		}

		async Task<bool> addOpCodeRef (object o, Component mcomp)
		{

			if (o is MethodDefinition) {
				MethodDefinition md = o as MethodDefinition;
				if (md.DeclaringType.IsInterface || md.IsConstructor) {
					return false;
				}
			}

			if (o is MethodReference || o is MethodDefinition) {
				var md = o as MethodReference;

				if (!md.DeclaringType.IsGenericInstance && !md.IsGenericInstance) {
					var declaringComp = await this.getTypeReferenceComp (md.DeclaringType, "Class");
					if (declaringComp != null) {
						var methodRef = addMethodComp (md, declaringComp);
						if (methodRef != null) {
							rep.AddReference (mcomp, methodRef, "Method call", model.GetReferenceTypeByName ("Calls"));
						}
					}
				}

			} else {

				Console.WriteLine ("Unknown type: " + o.GetType ());
			}
			return true;
		}

		private Component addMethodComp (MethodReference md, Component typeComp)
		{
			var methodComp = rep.AddComp (md.FullName, new Component (md.Name, workspace.Id, "", model.GetComponentTypeByName ("Method")), typeComp);
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
				var componentTarget = await getTypeReferenceComp (at.ParameterType, "Class");
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

		private void addRestService (CustomAttribute at, Component typeComp)
		{
			if (at.AttributeType.FullName.StartsWith ("Refit.")) {
				var service = at.ConstructorArguments [0].Value.ToString ();
				var operation = at.AttributeType.Name.Replace ("Attribute", "").ToLower ();
				var parentComp = rep.AddComp (service, new Component (service, workspace.Id, "", model.GetComponentTypeByName ("Service")));
				var comp = rep.AddComp (service + operation, new Component (operation, workspace.Id, "", model.GetComponentTypeByName ("Operation")), parentComp);
				rep.AddReference (typeComp, comp, operation, model.GetReferenceTypeByName ("Uses"));

				Console.WriteLine (operation + " " + service);
			}
		}

		private bool StoreExternalAssemblyDetail { get; set; }

		private async Task<Component> getTypeReferenceComp (TypeReference tr, String type)
		{
			var td = tr;
			if (td.IsArray) {
				td = td.GetElementType ();

			}
			if (td.Name == ".ctor" || td.Name == "T" || td.IsGenericParameter || td.IsGenericInstance) {

				return null;

			}

			var assembly = rep.GetComp (td.Scope.Name);

			var parentName = (td.IsNested) ? td.DeclaringType.Namespace : td.Namespace;
			var compName = (td.IsNested) ? td.DeclaringType.Name : td.Name;

			if (td.Module.Assembly.Name.Name != parentAssemply.Name) {
				var otherAssembly = await rep.GetComponentByPath (td.Module.Assembly.Name.Name, td.Name);
			}

			if (!StoreExternalAssemblyDetail && td.Module.Assembly.Name.Name != parentAssemply.Name) {
				return assembly;
			}

			if (assembly == null) {
				assembly = parentAssemply;
			}
			var parentComp = (!String.IsNullOrEmpty (parentName)) ? rep.AddComp (assembly.Fields ["_fullName"] + "/" + parentName, new Component (parentName, workspace.Id, "", model.GetComponentTypeByName ("Namespace")), assembly) : null;
			var comp = rep.AddComp (getCompName (td), new Component (compName, workspace.Id, "", model.GetComponentTypeByName (type)), parentComp);
			return comp;
		}

		/*private Component getTypeComp (TypeDefinition td)
		{
			if (td.Name == ".ctor") {
				return null;
			}
			var parentName = td.Namespace;
			var compName = td.Name;
			var assembly = rep.GetComp (td.Scope.Name);
			if (assembly == null) {
				assembly = parentAssemply;
			}
			var parentComp = (String.IsNullOrEmpty (parentName)) ? rep.AddComp (assembly.Fields ["_fullName"] + "/" + parentName, new Component (parentName, workspace.Id, "", model.GetComponentTypeByName ("Namespace")), assembly) : null;
			var type = (td.IsInterface) ? "Interface" : "Class";
			var comp = rep.AddComp (getCompName (td), new Component (compName, workspace.Id, "", model.GetComponentTypeByName (type)), parentComp);
			return comp;
		}*/

		private Component getAssemblyComp (AssemblyDefinition ad)
		{
			var c = new Component (ad.Name.Name, workspace.Id, ad.FullName, model.GetComponentTypeByName ("Assembly"));
			c.Fields = new Dictionary<string, object> ();
			c.Version = ad.Name.Version.ToString ();
			c.Fields.Add ("culture", ad.Name.Culture);
			return c;
		}


		private Component getAssemblyReference (AssemblyNameReference ad)
		{
			var c = new Component (ad.Name, workspace.Id, ad.FullName, model.GetComponentTypeByName ("Assembly"));
			c.Fields = new Dictionary<string, object> ();
			c.Version = ad.Version.ToString ();
			c.Fields.Add ("culture", ad.Culture);
			return c;
		}


		private String getCompName (TypeReference td)
		{
			var parent = td.Scope.Name;
			var cFullName = (td.IsGenericInstance || td.IsGenericParameter) ? td.GetElementType ().FullName : td.FullName;
			return parent + "/" + cFullName;
		}
			

	}
}
