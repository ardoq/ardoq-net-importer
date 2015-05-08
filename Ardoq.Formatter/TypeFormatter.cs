using System;
using System.Text;

namespace Ardoq.Formatter
{
    public class TypeFormatter
    {
        private readonly StringBuilder constructors = new StringBuilder();
        private readonly StringBuilder methods = new StringBuilder();

        public TypeFormatter()
        {
        }

        public string GetTypeInfo()
        {
            var text = "###Constructors\n" + constructors.ToString() + "###Methods\n" + methods.ToString();
            Console.WriteLine(text);
            return text;
        }

        public void WriteConstructorInfo(string text)
        {
            constructors.AppendLine(text);
        }

        public void WriteMethodInfo(string text)
        {
            methods.AppendLine(text);
        }
    }
}