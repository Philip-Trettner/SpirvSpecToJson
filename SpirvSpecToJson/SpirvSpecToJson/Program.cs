using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpirvSpecToJson
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            const string specUrl = @"https://www.khronos.org/registry/spir-v/specs/1.0/SPIRV.html";
            const string cacheFile = "spirv.html";
            const string jsonFile = "spirv.json";

            // download on demand
            if (!File.Exists(cacheFile))
            {
                Console.WriteLine("Spec not found, downloading.");
                var htmlClient = new HttpClient();

                Console.WriteLine("Downloading " + specUrl);
                var task = htmlClient.GetStringAsync(specUrl);
                task.Wait();
                File.WriteAllText(cacheFile, task.Result);
                Console.WriteLine("Finished.");
            }

            // cached spec
            Console.WriteLine("Read HTML from " + cacheFile);
            var html = File.ReadAllText(cacheFile);

            // html doc
            Console.WriteLine("Initialize HTML Doc");
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var root = doc.DocumentNode;

            // init json
            var specJson = new JObject();

            #region MetaData
            {
                var metaJson = new JObject();

                metaJson["Title"] = root.SelectSingleNode("//div[@id='header']/h1").InnerText;
                metaJson["Author"] = root.SelectSingleNode("//span[@id='author']").InnerText;
                metaJson["Revnumber"] = root.SelectSingleNode("//span[@id='revnumber']").InnerText;
                metaJson["LastUpdate"] = root.SelectSingleNode("//div[@id='footer-text']").LastChild.InnerText.Trim();

                specJson["Metadata"] = metaJson;
            }
            #endregion

            #region OpCode

            {
                var opcodeJson = new JArray();

                Console.WriteLine("Scanning OpCodes");
                foreach (var node in root.SelectNodes("//a"))
                {
                    // start with Op*
                    if (!node.Id.StartsWith("Op"))
                        continue;

                    // no spaces
                    if (node.Id.Contains(' '))
                        continue;

                    // length > 2
                    if (node.Id.Length <= 2)
                        continue;

                    // id[2] must be upper
                    if (char.IsLower(node.Id[2]))
                        continue;

                    // must be followed by <strong>OpCode</strong>
                    if (node.NextSibling == null || node.Id != node.NextSibling.InnerHtml)
                        continue;

                    var opcode = node.Id;
                    Console.WriteLine("  - " + opcode);

                    var opJson = new JObject();

                    opJson["Name"] = opcode;
                    opJson["Description"] = node.ParentNode.InnerHtml.Replace("<br>", "<br />").Trim();
                    opJson["DescriptionPlain"] = node.ParentNode.InnerText.Trim();

                    // Category
                    {
                        var n = node.ParentNode;

                        while (n.Name != "div")
                            n = n.ParentNode;

                        var category = n.ChildNodes.First(x => x.Name == "h4").InnerText;
                        category = category.Replace("Instructions", "");
                        category = category.Replace("(Removable)", "");
                        category = category.Substring(category.IndexOf(' '));
                        
                        opJson["Category"] = category.Trim().ToCamelCase();
                    }
                    var tr = node.ParentNode.ParentNode.ParentNode;
                    Debug.Assert(tr.Name == "tr");

                    var tdChildren = tr.ChildNodes.Count(n => n.Name == "td");

                    // capabilities
                    var caps = new JArray();
                    if (tdChildren == 2)
                    {
                        var capLinks = tr.ChildNodes.Last(n => n.Name == "td").SelectNodes(".//a");
                        foreach (var link in capLinks)
                            caps.Add(link.InnerText.Trim());
                    }
                    opJson["Capabilities"] = caps;

                    // opcode line
                    var optr = tr.ParentNode.ChildNodes.Last(n => n.Name == "tr");
                    Debug.Assert(optr != tr);
                    {
                        var hasResult = false;
                        var hasResultType = false;
                        var isVariableWC = false;

                        var tds = optr.ChildNodes.Where(n => n.Name == "td").ToArray();
                        Debug.Assert(tds.Length >= 2);

                        // word count
                        var wc = tds[0].InnerText.Trim();
                        if (wc.EndsWith(" + variable"))
                            isVariableWC = true;

                        // opcode NR
                        var opcodeNr = int.Parse(tds[1].InnerText.Trim());

                        // operands
                        var operands = new JArray();

                        for (var i = 2; i < tds.Length; ++i)
                        {
                            var td = tds[i];
                            var text = WebUtility.HtmlDecode(td.InnerText);
                            var operand = new JObject();

                            opJson["WordCount"] = wc;
                            opJson["WordCountFix"] = int.Parse(wc.Replace(" + variable", "").Trim());
                            opJson["OpCode"] = opcodeNr;
                            opJson["HasVariableWordCount"] = isVariableWC;


                            // Result
                            if (text == "Result <id>")
                            {
                                hasResult = true;
                                operand["Name"] = text.GetName();
                                operand["Type"] = "ID";
                                operands.Add(operand);
                            }
                            // Result Type
                            else if (text == "<id>\nResult Type")
                            {
                                hasResultType = true;
                                operand["Name"] = text.GetName();
                                operand["Type"] = "ID";
                                operands.Add(operand);
                            }
                            // Type: ID
                            else if (text.Contains("<id>") && !text.Contains(",") && !text.Contains("Optional"))
                            {
                                // Empty name => to "Object"
                                operand["Name"] = text.GetName() == "" ? "Object" : text.GetName();
                                operand["Type"] = "ID";
                                operands.Add(operand);
                            }
                            else if (text.Contains("literal, label <id>"))
                            {
                                operand["Name"] = "Target";
                                operand["Type"] = "Pair<LiteralNumber,ID>[]";
                                operands.Add(operand);
                            }
                            // For variable count of parameters
                            else if (text.Contains(","))
                            {
                                var a = text.GetParamsNameAndType();
                                operand["Name"] = a[0];
                                operand["Type"] = a[1];
                                operands.Add(operand);
                            }
                            // Linked Types
                            else if (td.InnerHtml.Contains("<a href="))
                            {
                                var a = text.GetLinkedNameAndType();
                                operand["Name"] = a[0].Replace(".","");
                                operand["Type"] = a[1];
                                operands.Add(operand);
                            }
                            // Optionals
                            else if (text.Contains("Optional"))
                            {
                                var a = text.GetLinkedNameAndType();
                                operand["Name"] = a[0] == "[Bias]" ? "Bias" : a[0];
                                operand["Type"] = "ID?";
                                operands.Add(operand);
                            }

                        }
                        opJson["HasResult"] = hasResult;
                        opJson["HasResultType"] = hasResultType;
                        opJson["Operands"] = operands;

                        opcodeJson.Add(opJson);
                    }

                    specJson["OpCodes"] = opcodeJson;
                }
            }

            #endregion

            #region Enums

           
            {
                // Array of Enums
                var enumJson = new JArray();

                Console.WriteLine("Scanning Enums");

                // Scan for "h3" => Header Type 3
                foreach (var node in root.SelectNodes("//h3"))
                {
                    // No empty npdes
                    if (node.Id == "")
                        continue;

                    // Only nodes that starts with "3."
                    if (!node.InnerText.StartsWith("3."))
                        continue;

                    // Magic Number and Instructions aren't Enums!
                    if (node.InnerText == "3.1. Magic Number" || node.InnerText == "3.27. Instructions")
                        continue;

                    var parent = node.ParentNode;
                    var table = parent.LastChild.PreviousSibling;
                    var tbody = table.LastChild.PreviousSibling;

                    // data that needs to be safed
                    var data = new JObject();

                    // Array of Values
                    var valuesArray = new JArray();

                    // name of Enum
                    {
                        var name = node.InnerText;

                        name = name.Substring(name.IndexOf(' '));

                        data["Name"] = name.Trim().ToCamelCase();
                    }

                    // Scan all rows
                    foreach (var tr in tbody.ChildNodes)
                    {
                        // No empty rows
                        if (tr.InnerText == "\n")
                            continue;

                        // Elements of Enums
                        var valueObj = new JObject();
                        var capabilities = new JArray();
                        var extraOperands = new JArray();


                        // Enums with values and names only. No Capabilities
                        if (tr.ChildNodes.Count == 5)
                        {
                            foreach (var td in tr.ChildNodes)
                            {
                                // no empty content  
                                if (td.InnerText == "\n")
                                    continue;

                                // Value Name
                                if (td.InnerHtml.Contains("<strong>"))
                                {
                                    var innerHtml = td.FirstChild.InnerHtml;
                                    var innerText = td.FirstChild.InnerText;

                                    var innerOuterHtml = td.FirstChild.FirstChild.OuterHtml;
                                    var innerInnerText = td.FirstChild.FirstChild.InnerText;


                                    valueObj["Name"] = innerInnerText;

                                    valueObj["Comment"] = innerText.Length == innerInnerText.Length
                                        ? ""
                                        : innerHtml.Substring(innerOuterHtml.Length + 4)
                                            .Trim()
                                            .Replace("<br>", "<br />");
                                    valueObj["CommentPlain"] = innerText.Substring(innerInnerText.Length).Trim();

                                }
                                else
                                {
                                    // Value
                                    valueObj["Value"] = int.Parse(td.InnerText);
                                }


                            }

                        }



                        // Enums with values, names and capabilities
                        if (tr.ChildNodes.Count == 7)
                        {
                            foreach (var td in tr.ChildNodes)
                            {
                                // No empty content
                                if (td.InnerText == "\n")
                                    continue;

                                // Value Name or Capabilities
                                if (td.InnerHtml.Contains("<strong>"))
                                {
                                    var innerHtml = td.FirstChild.InnerHtml;
                                    var innerText = td.FirstChild.InnerText;

                                    var innerOuterHtml = td.FirstChild.FirstChild.OuterHtml;
                                    var innerInnerText = td.FirstChild.FirstChild.InnerText;

                                    // Capabilities
                                    if (td.InnerHtml.Contains("#Cap"))
                                    {
                                        capabilities.Add(innerInnerText);
                                        var secondCap = innerText.Length == innerInnerText.Length
                                            ? ""
                                            : innerText.Substring(innerInnerText.Length + 1);
                                        if (!string.IsNullOrEmpty(secondCap))
                                            capabilities.Add(secondCap);
                                    }
                                    else
                                    {
                                        valueObj["Name"] = innerInnerText;

                                        valueObj["Comment"] = innerText.Length == innerInnerText.Length
                                            ? ""
                                            : innerHtml.Substring(innerOuterHtml.Length + 4)
                                                .Trim()
                                                .Replace("<br>", "<br />");
                                        valueObj["CommentPlain"] = innerText.Substring(innerInnerText.Length).Trim();
                                    }

                                }
                                else
                                {
                                    // Value
                                    if (!string.IsNullOrEmpty(td.InnerText))
                                        valueObj["Value"] = int.Parse(td.InnerText);
                                }


                            }

                        }


                        //Enums with values, names, capabilites and extra operands
                        if (tr.ChildNodes.Count > 8)
                        {
                            foreach (var td in tr.ChildNodes)
                            {
                                // No empty content
                                if (td.InnerText == "\n")
                                    continue;

                                // Value Name, Capabilities or Extra Operands
                                if (td.InnerHtml.Contains("<strong>"))
                                {
                                    var innerHtml = td.FirstChild.InnerHtml;
                                    var innerText = td.FirstChild.InnerText;

                                    var innerOuterHtml = td.FirstChild.FirstChild.OuterHtml;
                                    var innerInnerText = td.FirstChild.FirstChild.InnerText;

                                    // Capabilities
                                    if (td.InnerHtml.Contains("#Cap"))
                                    {
                                        capabilities.Add(innerInnerText);
                                        var secondCap = innerText.Length == innerInnerText.Length
                                            ? ""
                                            : innerText.Substring(innerInnerText.Length + 1);
                                        if (!string.IsNullOrEmpty(secondCap))
                                            capabilities.Add(secondCap);
                                    }
                                    else
                                    {
                                        valueObj["Name"] = innerInnerText;


                                        valueObj["Comment"] = innerText.Length == innerInnerText.Length
                                            ? ""
                                            : innerHtml.Substring(innerOuterHtml.Length + 4)
                                                .Trim()
                                                .Replace("<br>", "<br />");
                                        valueObj["CommentPlain"] = innerText.Substring(innerInnerText.Length).Trim();
                                    }

                                }
                                else
                                {
                                    int val;
                                    // Value
                                    if (int.TryParse(td.InnerText, out val))
                                        valueObj["Value"] = val;
                                    else
                                    {
                                        if (!String.IsNullOrEmpty(td.InnerText))
                                        {
                                            // Extra Operands                                       
                                            var innerText = td.FirstChild.InnerText.Trim();
                                            var innerInnerText = td.FirstChild.FirstChild.InnerText.Trim();

                                            var extraOperand = new JObject();

                                            extraOperand["Type"] = innerInnerText.ToCamelCase();
                                            extraOperand["Comment"] = innerInnerText == innerText
                                                ? ""
                                                : innerText.Substring(innerInnerText.Length + 1)
                                                    .Trim()
                                                    .Replace("<br>", "<br />");

                                            extraOperands.Add(extraOperand);

                                        }
                                    }

                                }


                            }

                        }



                        // Add Capabilites and ExtraOperands
                        valueObj["Capabilities"] = capabilities;
                        valueObj["ExtraOperands"] = extraOperands;

                        // Add Values
                        valuesArray.Add(valueObj);
                    }


                    data["Values"] = valuesArray;

                    data["Comment"] = node.NextSibling.NextSibling.FirstChild.InnerHtml.Replace("<br>", "<br />");
                    data["CommentPlain"] = node.NextSibling.NextSibling.InnerText;


                    enumJson.Add(data);

                    specJson["Enums"] = enumJson;

                }
            }

            #endregion

            // save json
            Console.WriteLine("Writing result json");
            File.WriteAllText(jsonFile, specJson.ToString(Formatting.Indented));
        }
    }
}
