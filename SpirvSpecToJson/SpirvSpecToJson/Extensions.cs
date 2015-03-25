using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SpirvSpecToJson
{
    public static class Extensions
    {
        /// <summary>
        /// Get the name of an operand with the type ID
        /// </summary>
        /// <param name="text"></param>
        /// <returns>name of operand</returns>
        public static string GetName( this string text)
        {
            // 2 Cases:
            //
            // <id> Name
            // Name <id>

            // Returns just the name
            return text.Replace("<id>", "").Trim();

        }
        /// <summary>
        /// Get the Name of the operand list with the type ID.
        /// 1. Element: Name, 2. Element: Type
        /// </summary>
        /// <param name="text">analyzable text</param>
        /// <returns>1. Element: Name
        /// 2. Element: Type</returns>
        public static string[] GetParamsNameAndType(this string text)
        {
            var a = new string[2];

            if (text.Contains("<id>"))
            {
                // Format:
                //
                // "<id>, <id>, ...
                // Name, Name"

                var s = text.Replace("<id>, <id>, …", "").Trim();

                // Just get one name
                s = s.Split()[0].Replace(",", "");

                if (string.IsNullOrEmpty(s))
                    s = "Operands";

                // Add a "s" for plural if necessary
                if (!s.EndsWith("s"))
                    s += "s";

                a[0] = s;
                a[1] = "ID[]";
            }

            if (text.Contains("literal"))
            {
                // Format:
                //
                // "literal, literal, ...
                // Name"

                var s = text.Replace("literal, literal, …", "").Trim();

                // When text is like
                //
                // "literal, literal, ... See Name"
                //
                // then set name to ExtraOperands
                if (text.Contains("See"))
                    a[0] = "ExtraOperands";
                else
                    a[0] = s;

                // Type: LiteralNumber
                a[1] = "LiteralNumber[]";

            }


            return a;

        }
        /// <summary>
        /// Get the type name of operands
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static string GetLinkedType(this string text)
        {
            var s = new StringBuilder();
            string[] sarray = new string[2];

            for (int i = 0; i < text.Length; i++)
            {
                // TODO
            }
            return s.ToString();
        }
    }
}
