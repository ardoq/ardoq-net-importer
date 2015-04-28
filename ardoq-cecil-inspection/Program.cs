using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Reflection;
using System.IO;
using Mono.Cecil;
using Ardoq.Util;
using Ardoq.Models;
using Ardoq.AssemblyInspection;
using Ardoq.Service.Interface;
using CommandLine;
using Moq;

namespace Ardoq
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var commandRunner = new CommandRunner();

            var command = new CommandOptions();
            var result = Parser.Default.ParseArguments(args, command);
            if (result)
            {
                commandRunner.Run(command).Wait();
            }
            else
            {
                if (command.Token == null)
                {
                    Console.WriteLine("Supply authentication token: -t <token>");
                }
                if (command.AssemblyPath == null)
                {
                    Console.WriteLine("Add assembly filename: -a <path/assembly.dll>");
                }
            }
        }
    }
}
