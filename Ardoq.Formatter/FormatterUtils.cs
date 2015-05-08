using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ardoq.Formatter
{
    public static class FormatterUtils
    {
        public static string MakeMarkup(String value)
        {
            if (!String.IsNullOrEmpty(value))
            {
                return value.Replace("<", "&lt;").Replace(">", "&gt;").Replace("`", "\\`");
            }
            return "";
        }
    }
}
