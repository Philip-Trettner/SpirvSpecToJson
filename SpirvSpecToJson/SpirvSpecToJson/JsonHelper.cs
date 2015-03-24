using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpirvSpecToJson
{
    class JsonHelper
    {
        public static string GetName(string text)
        {
            var s = new StringBuilder();

            // Case: Name <ID>

            if (text[0] != '<')
            {
                foreach (char t in text)
                {
                    if (t == '<')
                        break;
                    s.Append(t);
                }
                var st1 = s.ToString().Trim();

                return st1;
            }

            // Case: <ID> Name

            for (int i = 4; i < text.Length; i++)
            {
                s.Append(text[i]);
            }
            var st2 = s.ToString().Trim();

            return st2;
        }

        public static string GetParamsName(string text)
        {
            var s = new StringBuilder();

            for (int i = 14; i < text.Length; i++)
            {
                if (text[i] != ',' && text[i] != ' ')
                    s.Append(text[i]);
                else
                    break;
            }

            var st = s.ToString();

            if (st == String.Empty)
                st = "Operands";

            if (st[st.Length-1] != 's')
                st += "s";

            return st;
        }
    }
}
