using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ardoq.Fomatter
{
    public class Formatter
    {
        public string MakeMarkup(String value)
        {
            if (!String.IsNullOrEmpty(value))
            {
                return value.Replace("<", "&lt;").Replace(">", "&gt;").Replace("`", "\\`");
            }
            return "";
        }
    }
}
