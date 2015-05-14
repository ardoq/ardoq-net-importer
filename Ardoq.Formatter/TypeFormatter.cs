using System;
using System.Text;

namespace Ardoq.Formatter
{
    public class TypeFormatter
    {
        private readonly StringBuilder description = new StringBuilder();
        private readonly StringBuilder constructors = new StringBuilder();
        private readonly StringBuilder methods = new StringBuilder();

        public string GetTypeInfo()
        {
            var builder = new StringBuilder();
            if (description.Length > 0)
                builder.AppendLine(description.ToString());
            builder.AppendLine("###Constructors");
            builder.Append(constructors);
            builder.AppendLine("###Methods");
            builder.Append(methods);
#if DEBUG
            Console.WriteLine(builder.ToString());
#endif
            return  builder.ToString();
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

        public void WriteConstructorInfo(string text)
        {
            if (!string.IsNullOrEmpty(text))
                constructors.AppendLine(text);
        }

        public void WriteMethodInfo(string text)
        {
            if (!string.IsNullOrEmpty(text))
                methods.AppendLine(text);
        }
    }
}