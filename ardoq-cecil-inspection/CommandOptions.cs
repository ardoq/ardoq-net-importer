using System;
using System.Reflection;
using CommandLine;
using CommandLine.Text;

namespace Ardoq
{
    public class CommandOptions
    {
        [Option('a', "AssemblyPath", Required = true,
            HelpText = "Input assembly to be documented.")]
        public string AssemblyPath { get; set; }

        [Option('t', "token", Required = true,
            HelpText = "Authentication token")]
        public string Token { get; set; }


        [Option('m', "model", Required = false, DefaultValue = ".Net",
            HelpText = "Name of different model, must be based on .Net")]
        public string ModelName { get; set; }

        [Option('o', "organization", Required = false, DefaultValue = "ardoq",
            HelpText = "Organization to store data in")]
        public string Org { get; set; }


        [Option('h', "hostName", Required = false, DefaultValue = "https://app.ardoq.com",
            HelpText = "The Ardoq host")]
        public string HostName { get; set; }

        [Option('r', "selfReference", Required = false, DefaultValue = false,
            HelpText = "Allow self references (not fully supported in Ardoq.)")]
        public bool SelfReference { get; set; }


        [Option('s', "skipMethods", Required = false, DefaultValue = false,
            HelpText = "Don't add Method pages")]
        public bool SkipAddMethodToDocs { get; set; }

        [Option('i', "opcodeInstruction", Required = false, DefaultValue = true,
            HelpText = "Analyse OpCode instructions in methods")]
        public bool AddInstructionReferences { get; set; }

        [Option('e', "skipStoreExternalAssembly", Required = false, DefaultValue = false,
            HelpText = "Skip Store external assembly calls")]
        public bool SkipStoreExternalAssemblyDetail { get; set; }

        [Option('d', "detail", Required = false, DefaultValue = false,
            HelpText = "Include private members")]
        public bool IncludePrivate { get; set; }

        [Option('n', "notifyMe", Required = false, DefaultValue = false,
            HelpText = "Notify me by mail")]
        public bool NotifyByMail { get; set; }

        [Option('f', "folderName", Required = false, DefaultValue = ".NET Assemblies",
            HelpText = "Folder name, set to blank to ignore")]
        public string FolderName { get; set; }


        [HelpOption]
        public string GetUsage()
        {
            var help = new HelpText
            {
                Heading = new HeadingInfo("Ardoq Assembly Doc", Assembly.GetExecutingAssembly().GetName().Version.ToString()),
                Copyright = new CopyrightInfo("Ardoq AS", 2015),
                AdditionalNewLineAfterOption = true,
                AddDashesToOption = true
            };
            help.AddOptions(this);
            return help;
        }
    }
}