using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ardoq.Formatter
{
    public class MethodFormatter
    {
        private readonly StringBuilder description = new StringBuilder();
        private readonly StringBuilder definition = new StringBuilder();

        public string GetMethodInfo()
        {
            var builder = new StringBuilder();
            builder.Append(definition);
            if (description.Length > 0)
                builder.AppendLine(description.ToString());
            return builder.ToString();
        }

        public void WriteDescriptionInfo(string text, string caption = null)
        {
            if (!string.IsNullOrEmpty(text))
            {
                if (!string.IsNullOrEmpty(caption))
                    description.AppendLine("####" + caption);
                description.AppendLine(text.Trim('\r', '\n', '\t', ' '));
            }
        }

        public void WriteDefinitionInfo(string methodName, string declaringTypeName, string returnTypeName, 
            bool isConstructor, bool isProperty, IEnumerable<Tuple<string, string>> methodParameters)
        {
            definition.Append("####");
            if (isConstructor)
            {
                definition.Append("new ");
                definition.Append(declaringTypeName);
            }
            else
            {
                definition.Append(returnTypeName);
                definition.Append(" ");
                definition.Append(methodName);
            }

            if (!isProperty)
            {
                definition.Append("(");
                if (methodParameters != null && methodParameters.Any())
                {
                    foreach (var p in methodParameters)
                    {
                        definition.Append(p.Item2);
                        definition.Append(" ");
                        definition.Append(p.Item1);
                        definition.Append(", ");
                    }
                    definition.Remove(definition.Length - 2, 2);
                }
                definition.Append(")");
            }
            definition.AppendLine();
        }
    }
}