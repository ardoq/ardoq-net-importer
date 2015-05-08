using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ardoq.Formatter
{
    public class MethodFormatter
    {
        private readonly StringBuilder textBuilder = new StringBuilder();

        public MethodFormatter()
        {
        }

        public string GetMethodInfo()
        {
            return textBuilder.ToString();
        }

        public void WriteMethodHeader(string methodName, string declaringTypeName, string returnTypeName, 
            bool isConstructor, IEnumerable<Tuple<string, string>> methodParameters)
        {
            textBuilder.Append("####");
            if (isConstructor)
            {
                textBuilder.Append("new ");
                textBuilder.Append(declaringTypeName);
            }
            else
            {
                textBuilder.Append(returnTypeName);
                textBuilder.Append(" ");
                textBuilder.Append(methodName);
            }
            textBuilder.Append("(");

            if (methodParameters != null && methodParameters.Any())
            {
                foreach (var p in methodParameters)
                {
                    textBuilder.Append(p.Item2);
                    textBuilder.Append(" ");
                    textBuilder.Append(p.Item1);
                    textBuilder.Append(", ");
                }
                textBuilder.Remove(textBuilder.Length - 2, 1);
            }

            textBuilder.AppendLine(")");
        }
    }
}