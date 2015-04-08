﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.SqlServer.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpirvSpecToJson
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            const string specUrl = @"https://www.khronos.org/registry/spir-v/specs/1.0/SPIRV.html";
            const string specExtUrlGLSL = @"https://www.khronos.org/registry/spir-v/specs/1.0/GLSL.std.450.html";
            const string specExtUrlOpenCL12 = @"https://www.khronos.org/registry/spir-v/specs/1.0/OpenCL.std.12.html";
            const string specExtUrlOpenCL20 = @"https://www.khronos.org/registry/spir-v/specs/1.0/OpenCL.std.20.html";
            const string specExtUrlOpenCL21 = @"https://www.khronos.org/registry/spir-v/specs/1.0/OpenCL.std.21.html";
            const string cacheFile = "spirv.html";
            const string cacheFileExtGLSL = "spirvExtGLSL.html";
            const string cacheFileExtOpenCL12 = "spirvExtOpenCL12.html";
            const string cacheFileExtOpenCL20 = "spirvExtOpenCL20.html";
            const string cacheFileExtOpenCL21 = "spirvExtOpenCL21.html";
            const string jsonFile = "spirv.json";


            var dic = new Dictionary<string, string>
            {
                {specUrl, cacheFile},
                {specExtUrlGLSL, cacheFileExtGLSL},
                {specExtUrlOpenCL12, cacheFileExtOpenCL12},
                {specExtUrlOpenCL20, cacheFileExtOpenCL20},
                {specExtUrlOpenCL21, cacheFileExtOpenCL21}
            };

            // download on demand
            //TODO: Only download what needed
            if (!File.Exists(cacheFile) || !File.Exists(cacheFileExtGLSL) || !File.Exists(cacheFileExtOpenCL12) || !File.Exists(cacheFileExtOpenCL20) || !File.Exists(cacheFileExtOpenCL21))
            {
                Console.WriteLine("Spec not found, downloading.");
                var htmlClient = new HttpClient();

                foreach (var t in dic)
                {
                    Console.WriteLine("Downloading " + t.Key);
                    var task = htmlClient.GetStringAsync(t.Key);
                    task.Wait();
                    File.WriteAllText(t.Value, task.Result);

                }

                Console.WriteLine("Finished.");
            }

            #region Spec

            var specJson = new JObject();
            {
                // cached spec
                Console.WriteLine("Read HTML from " + cacheFile);
                var html = File.ReadAllText(cacheFile);

                // html doc
                Console.WriteLine("Initialize HTML Doc");
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                var root = doc.DocumentNode;

                // init json

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
                                    operand["Name"] = text.GetName(true);
                                    operand["Type"] = "ID";
                                    operands.Add(operand);
                                }
                                // Result Type
                                else if (text == "<id>\nResult Type")
                                {
                                    hasResultType = true;
                                    operand["Name"] = text.GetName(true);
                                    operand["Type"] = "ID";
                                    operands.Add(operand);
                                }
                                // Type: ID
                                else if (text.Contains("<id>") && !text.Contains(",") && !text.Contains("Optional"))
                                {
                                    // Empty name => to "Object"
                                    operand["Name"] = text.GetName(true) == "" ? "Object" : text.GetName(true);
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
                                    var a = text.GetLinkedNameAndType(true, true);
                                    operand["Name"] = a[0].Replace(".", "");
                                    operand["Type"] = a[1];
                                    operands.Add(operand);
                                }
                                // Optionals
                                else if (text.Contains("Optional"))
                                {
                                    var a = text.GetLinkedNameAndType(true, true);
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
                            else if (tr.ChildNodes.Count == 7)
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
                            else if (tr.ChildNodes.Count > 8)
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

            }

            #endregion

            #region Extensions

            {
                var extJson = new JArray();

                #region GLSL

                {
                    // Load File

                    // cached spec
                    Console.WriteLine("Read HTML from " + cacheFileExtGLSL);
                    var htmlExt = File.ReadAllText(cacheFileExtGLSL);

                    // html doc
                    Console.WriteLine("Initialize HTML Doc");
                    var docExt = new HtmlDocument();
                    docExt.LoadHtml(htmlExt);
                    var rootExt = docExt.DocumentNode;

                    var extGLSL = new JObject();

                    // Metadata
                    {
                        var metaData = new JObject();

                        metaData["Language"] = "GLSL";
                        metaData["Version"] = 450;
                        metaData["Title"] = rootExt.SelectSingleNode("//div[@id='header']/h1").InnerText;
                        metaData["Author"] = rootExt.SelectSingleNode("//span[@id='author']").InnerText;
                        metaData["Revnumber"] = rootExt.SelectSingleNode("//span[@id='revnumber']").InnerText;
                        metaData["LastUpdate"] = rootExt.SelectSingleNode("//div[@id='footer-text']").LastChild.InnerText.Trim();

                        extGLSL["Metadata"] = metaData;
                    }

                    // Extended Instructions
                    {
                        var extendedInstructions = new JArray();

                        foreach (var tbody in rootExt.SelectNodes("//tbody"))
                        {
                            var extInst = new JObject();

                            // Table "Extended Instruction Name" hasn't extended instructions
                            if (tbody.InnerText.Contains("Extended Instruction Name"))
                                continue;

                            var tr = tbody.FirstChild.NextSibling;
                            var td = tr.FirstChild.NextSibling;
                            var name = td.FirstChild.FirstChild.InnerText;
                            var innerHtml = td.FirstChild.InnerHtml;

                            extInst["Name"] = name.ToCamelCase();
                            extInst["OriginalName"] = name;

                            // Description
                            var desc = innerHtml == name ? "" : innerHtml.Substring(name.Length + "<strong></strong>".Length).Trim().Replace("<br>", "<br />");

                            if (desc.StartsWith("<br />\n<br />\n"))
                                desc = desc.Substring("<br />\n<br />\n".Length);

                            extInst["Description"] = desc;
                            extInst["DescriptionPlain"] = td.FirstChild.InnerText.Substring(name.Length).Trim();

                            extendedInstructions.Add(extInst);

                            // Number and Params
                            {
                                var row = tbody.LastChild.PreviousSibling;

                                var operands = new JArray();

                                foreach (var column in row.ChildNodes)
                                {
                                    int nr;
                                    var operand = new JObject();

                                    // No empty content
                                    if (column.InnerText == "" || column.InnerText == "\n")
                                        continue;

                                    // Number
                                    else if (int.TryParse(column.InnerText, out nr))
                                        extInst["Number"] = nr;
                                    // Operands
                                    else
                                    {
                                        operand["Name"] = WebUtility.HtmlDecode(column.InnerText).GetName(false);
                                        operand["Type"] = "ID";
                                        operands.Add(operand);
                                    }
                                }

                                extInst["Operands"] = operands;
                            }

                        }

                        extGLSL["ExtendedInstructions"] = extendedInstructions;
                    }

                    extJson.Add(extGLSL);

                }

                #endregion

                #region CL12
                {
                    // Load File

                    // cached spec
                    Console.WriteLine("Read HTML from " + cacheFileExtOpenCL12);
                    var htmlExt = File.ReadAllText(cacheFileExtOpenCL12);

                    // html doc
                    Console.WriteLine("Initialize HTML Doc");
                    var docExt = new HtmlDocument();
                    docExt.LoadHtml(htmlExt);
                    var rootExt = docExt.DocumentNode;

                    var extCL12 = new JObject();

                    // Metadata
                    {
                        var metaData = new JObject();

                        metaData["Language"] = "Open CL";
                        metaData["Version"] = 1.2;
                        metaData["Title"] = rootExt.SelectSingleNode("//div[@id='header']/h1").InnerText;
                        metaData["Author"] = rootExt.SelectSingleNode("//span[@id='author']").InnerText;
                        metaData["Revnumber"] = rootExt.SelectSingleNode("//span[@id='revnumber']").InnerText;
                        metaData["LastUpdate"] = rootExt.SelectSingleNode("//div[@id='footer-text']").LastChild.InnerText.Trim();

                        extCL12["Metadata"] = metaData;
                    }

                    // Extended Instructions
                    {
                        var extendedInstructions = new JArray();

                        foreach (var tbody in rootExt.SelectNodes("//tbody"))
                        {
                            var extInst = new JObject();

                            var tr = tbody.FirstChild.NextSibling;
                            var td = tr.FirstChild.NextSibling;


                            // Enums => ignore
                            {

                                if (tbody.ParentNode.ParentNode.FirstChild.NextSibling.Id ==
                                    "_a_id_imageformatenc_a_image_format_encoding")
                                {
                                    continue;
                                }
                            }

                            var name = td.FirstChild.FirstChild.NextSibling.InnerText;

                            extInst["Name"] = name.ToCamelCase();
                            extInst["OriginalName"] = name;

                            // Description
                            {
                                var comment = new StringBuilder();
                                var commentPlain = new StringBuilder();

                                // The complete comment is written in different rows => get all
                                foreach (var commentBlock in td.ChildNodes)
                                {
                                    comment.Append(commentBlock.InnerHtml);
                                    commentPlain.Append(commentBlock.InnerText);
                                }

                                var comInnerHtml = comment.ToString();
                                var comInnerText = commentPlain.ToString();

                                // Replace <br> to <br />
                                comInnerHtml = comInnerHtml == name
                                    ? ""
                                    : comInnerHtml.Substring(2 * name.Length +
                                                             "<a id=\"acos\"></a><strong></strong>".Length)
                                        .Trim()
                                        .Replace("<br>", "<br />");

                                // Delete "/n" or "<br /> at beginning and at the end
                                if (comInnerHtml.StartsWith("<br />\n"))
                                    comInnerHtml = comInnerHtml.Substring("<br />\n".Length);
                                if (comInnerHtml.EndsWith("\n<br />"))
                                    comInnerHtml = comInnerHtml.Remove(comInnerHtml.Length - "\n<br />".Length);
                                if (comInnerText.StartsWith(name + "\n\n"))
                                    comInnerText = comInnerText.Substring(name.Length + "\n\n".Length);
                                if (comInnerText.EndsWith("\n"))
                                    comInnerText = comInnerText.Remove(comInnerText.Length - "\n".Length);

                                extInst["Description"] = comInnerHtml;
                                extInst["DescriptionPlain"] = comInnerText;
                            }


                            // Category
                            var cat = tbody.ParentNode.ParentNode.FirstChild.NextSibling.InnerText;
                            {
                                cat = cat.Substring(cat.IndexOf(" ")).Trim().ToCamelCase();
                                extInst["Category"] = cat;
                            }

                            // Number and Params
                            {
                                var row = tbody.LastChild.PreviousSibling;

                                var operands = new JArray();

                                //TODO: implement ImageEncoding
                                if (cat == "ImageEncoding" || cat == "SamplerEncoding")
                                    continue;

                                // Start at 6th column (Number)
                                // After Number there are only params
                                for (int i = 11; i < row.ChildNodes.Count; i++)
                                {
                                    var column = row.ChildNodes[i];
                                    int nr;
                                    var operand = new JObject();

                                    // No empty content
                                    if (column.InnerText == "" || column.InnerText == "\n")
                                        continue;

                                    // Number
                                    else if (int.TryParse(column.InnerText, out nr))
                                        extInst["Number"] = nr;
                                    // Operands
                                    else
                                    {
                                        // LiteralNumber
                                        if (column.InnerText.Contains("Literal Number"))
                                        {
                                            var a = column.InnerText.GetLinkedNameAndType(false, true);
                                            operand["Name"] = a[0];
                                            operand["Type"] = a[1];
                                            operands.Add(operand);
                                        }
                                        // printf -> addionalArguemnts as ID Array
                                        else if (column.InnerText.StartsWith("&lt;id&gt;, &lt;id&gt;,"))
                                        {
                                            operand["Name"] = "AdditionalArguments";
                                            operand["Type"] = "ID[]";
                                        }
                                        // IDs
                                        else
                                        {
                                            operand["Name"] = WebUtility.HtmlDecode(column.InnerText).GetName(false);
                                            operand["Tpe"] = "ID";
                                            operands.Add(operand);
                                        }
                                    }
                                }

                                extInst["Operands"] = operands;
                            }

                            extendedInstructions.Add(extInst);

                        }
                        extCL12["ExtendedInstructions"] = extendedInstructions;
                    }

                    // Enums
                    {
                        var enums = new JArray();

                        foreach (var tbody in rootExt.SelectNodes("//tbody"))
                        {
                            var enu = new JObject();

                            // Enums ID
                            if (tbody.ParentNode.ParentNode.FirstChild.NextSibling.Id ==
                                "_a_id_imageformatenc_a_image_format_encoding")
                            {
                                var values = new JArray();

                                Debug.Assert(tbody.ParentNode.ChildNodes.Count > 5);

                                enu["Name"] = tbody.ParentNode.ChildNodes[5].InnerText.Trim();

                                // Values
                                foreach (var tr in tbody.ChildNodes)
                                {
                                    var val = new JObject();
                                    if (tr.Name == "tr")
                                    {
                                        Debug.Assert(tr.ChildNodes.Count > 3);

                                        val["Value"] = int.Parse(tr.ChildNodes[1].InnerText);
                                        val["Name"] = tr.ChildNodes[3].InnerText;

                                        values.Add(val);
                                    }
                                }

                                enu["Values"] = values;

                                enums.Add(enu);

                            }
                        }

                        extCL12["Enums"] = enums;
                    }

                    extJson.Add(extCL12);

                }

                #endregion

                #region CL20
                {
                    // Load File

                    // cached spec
                    Console.WriteLine("Read HTML from " + cacheFileExtOpenCL20);
                    var htmlExt = File.ReadAllText(cacheFileExtOpenCL20);

                    // html doc
                    Console.WriteLine("Initialize HTML Doc");
                    var docExt = new HtmlDocument();
                    docExt.LoadHtml(htmlExt);
                    var rootExt = docExt.DocumentNode;

                    var extCL20 = new JObject();

                    // Metadata
                    {
                        var metaData = new JObject();

                        metaData["Language"] = "Open CL";
                        metaData["Version"] = 2.0;
                        metaData["Title"] = rootExt.SelectSingleNode("//div[@id='header']/h1").InnerText;
                        metaData["Author"] = rootExt.SelectSingleNode("//span[@id='author']").InnerText;
                        metaData["Revnumber"] = rootExt.SelectSingleNode("//span[@id='revnumber']").InnerText;
                        metaData["LastUpdate"] = rootExt.SelectSingleNode("//div[@id='footer-text']").LastChild.InnerText.Trim();

                        extCL20["Metadata"] = metaData;
                    }

                    // Extended Instructions
                    {
                        var extendedInstructions = new JArray();

                        foreach (var tbody in rootExt.SelectNodes("//tbody"))
                        {
                            var extInst = new JObject();

                            var tr = tbody.FirstChild.NextSibling;
                            var td = tr.FirstChild.NextSibling;
                            
                            // Enums => ignore
                            {

                                if (tbody.ParentNode.ParentNode.FirstChild.NextSibling.Id ==
                                    "_a_id_imageformatenc_a_image_format_encoding")
                                {
                                    continue;
                                }
                            }

                            var name = td.FirstChild.FirstChild.NextSibling.InnerText;

                            extInst["Name"] = name.ToCamelCase();
                            extInst["OriginalName"] = name;

                            // Description
                            {
                                var comment = new StringBuilder();
                                var commentPlain = new StringBuilder();

                                // The complete comment is written in different rows => get all
                                foreach (var commentBlock in td.ChildNodes)
                                {
                                    comment.Append(commentBlock.InnerHtml);
                                    commentPlain.Append(commentBlock.InnerText);
                                }

                                var comInnerHtml = comment.ToString();
                                var comInnerText = commentPlain.ToString();

                                // Replace <br> to <br />
                                comInnerHtml = comInnerHtml == name
                                    ? ""
                                    : comInnerHtml.Substring(2 * name.Length +
                                                             "<a id=\"acos\"></a><strong></strong>".Length)
                                        .Trim()
                                        .Replace("<br>", "<br />");

                                // Delete "/n" or "<br /> at beginning and at the end
                                if (comInnerHtml.StartsWith("<br />\n"))
                                    comInnerHtml = comInnerHtml.Substring("<br />\n".Length);
                                if (comInnerHtml.EndsWith("\n<br />"))
                                    comInnerHtml = comInnerHtml.Remove(comInnerHtml.Length - "\n<br />".Length);
                                if (comInnerText.StartsWith(name + "\n\n"))
                                    comInnerText = comInnerText.Substring(name.Length + "\n\n".Length);
                                if (comInnerText.EndsWith("\n"))
                                    comInnerText = comInnerText.Remove(comInnerText.Length - "\n".Length);

                                extInst["Description"] = comInnerHtml;
                                extInst["DescriptionPlain"] = comInnerText;
                            }


                            // Category
                            var cat = tbody.ParentNode.ParentNode.FirstChild.NextSibling.InnerText;
                            {
                                cat = cat.Substring(cat.IndexOf(" ")).Trim().ToCamelCase();
                                extInst["Category"] = cat;
                            }

                            // Number and Params
                            {
                                var row = tbody.LastChild.PreviousSibling;

                                var operands = new JArray();

                                //TODO: implement ImageEncoding
                                if (cat == "ImageEncoding" || cat == "SamplerEncoding")
                                    continue;

                                // Start at 6th column (Number)
                                // After Number there are only params
                                for (int i = 11; i < row.ChildNodes.Count; i++)
                                {
                                    var column = row.ChildNodes[i];
                                    int nr;
                                    var operand = new JObject();

                                    // No empty content
                                    if (column.InnerText == "" || column.InnerText == "\n")
                                        continue;

                                    // Number
                                    else if (int.TryParse(column.InnerText, out nr))
                                        extInst["Number"] = nr;
                                    // Operands
                                    else
                                    {
                                        // LiteralNumber
                                        if (column.InnerText.Contains("Literal Number"))
                                        {
                                            var a = column.InnerText.GetLinkedNameAndType(false, true);
                                            operand["Name"] = a[0];
                                            operand["Type"] = a[1];
                                            operands.Add(operand);
                                        }
                                        // printf -> addionalArguemnts as ID Array
                                        else if (column.InnerText.StartsWith("&lt;id&gt;, &lt;id&gt;,"))
                                        {
                                            operand["Name"] = "AdditionalArguments";
                                            operand["Type"] = "ID[]";
                                        }
                                        // IDs
                                        else
                                        {
                                            operand["Name"] = WebUtility.HtmlDecode(column.InnerText).GetName(false);
                                            operand["Tpe"] = "ID";
                                            operands.Add(operand);
                                        }
                                    }
                                }

                                extInst["Operands"] = operands;
                            }

                            extendedInstructions.Add(extInst);

                        }
                        extCL20["ExtendedInstructions"] = extendedInstructions;
                    }

                    // Enums
                    {
                        var enums = new JArray();

                        foreach (var tbody in rootExt.SelectNodes("//tbody"))
                        {
                            var enu = new JObject();

                            // Enums ID
                            if (tbody.ParentNode.ParentNode.FirstChild.NextSibling.Id ==
                                "_a_id_imageformatenc_a_image_format_encoding")
                            {
                                var values = new JArray();

                                Debug.Assert(tbody.ParentNode.ChildNodes.Count > 5);

                                enu["Name"] = tbody.ParentNode.ChildNodes[5].InnerText.Trim();

                                // Values
                                foreach (var tr in tbody.ChildNodes)
                                {
                                    var val = new JObject();
                                    if (tr.Name == "tr")
                                    {
                                        Debug.Assert(tr.ChildNodes.Count > 3);

                                        val["Value"] = int.Parse(tr.ChildNodes[1].InnerText);
                                        val["Name"] = tr.ChildNodes[3].InnerText;

                                        values.Add(val);
                                    }
                                }

                                enu["Values"] = values;

                                enums.Add(enu);

                            }
                        }

                        extCL20["Enums"] = enums;
                    }

                    extJson.Add(extCL20);

                }

                #endregion

                #region CL21
                {
                    // Load File

                    // cached spec
                    Console.WriteLine("Read HTML from " + cacheFileExtOpenCL21);
                    var htmlExt = File.ReadAllText(cacheFileExtOpenCL21);

                    // html doc
                    Console.WriteLine("Initialize HTML Doc");
                    var docExt = new HtmlDocument();
                    docExt.LoadHtml(htmlExt);
                    var rootExt = docExt.DocumentNode;

                    var extCL21 = new JObject();

                    // Metadata
                    {
                        var metaData = new JObject();

                        metaData["Language"] = "Open CL";
                        metaData["Version"] = 2.0;
                        metaData["Title"] = rootExt.SelectSingleNode("//div[@id='header']/h1").InnerText;
                        metaData["Author"] = rootExt.SelectSingleNode("//span[@id='author']").InnerText;
                        metaData["Revnumber"] = rootExt.SelectSingleNode("//span[@id='revnumber']").InnerText;
                        metaData["LastUpdate"] = rootExt.SelectSingleNode("//div[@id='footer-text']").LastChild.InnerText.Trim();

                        extCL21["Metadata"] = metaData;
                    }

                    // Extended Instructions
                    {
                        var extendedInstructions = new JArray();

                        foreach (var tbody in rootExt.SelectNodes("//tbody"))
                        {
                            var extInst = new JObject();

                            var tr = tbody.FirstChild.NextSibling;
                            var td = tr.FirstChild.NextSibling;

                            // Enums => ignore
                            {

                                if (tbody.ParentNode.ParentNode.FirstChild.NextSibling.Id ==
                                    "_a_id_imageformatenc_a_image_format_encoding")
                                {
                                    continue;
                                }
                            }

                            var name = td.FirstChild.FirstChild.NextSibling.InnerText;

                            extInst["Name"] = name.ToCamelCase();
                            extInst["OriginalName"] = name;

                            // Description
                            {
                                var comment = new StringBuilder();
                                var commentPlain = new StringBuilder();

                                // The complete comment is written in different rows => get all
                                foreach (var commentBlock in td.ChildNodes)
                                {
                                    comment.Append(commentBlock.InnerHtml);
                                    commentPlain.Append(commentBlock.InnerText);
                                }

                                var comInnerHtml = comment.ToString();
                                var comInnerText = commentPlain.ToString();

                                // Replace <br> to <br />
                                comInnerHtml = comInnerHtml == name
                                    ? ""
                                    : comInnerHtml.Substring(2 * name.Length +
                                                             "<a id=\"acos\"></a><strong></strong>".Length)
                                        .Trim()
                                        .Replace("<br>", "<br />");

                                // Delete "/n" or "<br /> at beginning and at the end
                                if (comInnerHtml.StartsWith("<br />\n"))
                                    comInnerHtml = comInnerHtml.Substring("<br />\n".Length);
                                if (comInnerHtml.EndsWith("\n<br />"))
                                    comInnerHtml = comInnerHtml.Remove(comInnerHtml.Length - "\n<br />".Length);
                                if (comInnerText.StartsWith(name + "\n\n"))
                                    comInnerText = comInnerText.Substring(name.Length + "\n\n".Length);
                                if (comInnerText.EndsWith("\n"))
                                    comInnerText = comInnerText.Remove(comInnerText.Length - "\n".Length);

                                extInst["Description"] = comInnerHtml;
                                extInst["DescriptionPlain"] = comInnerText;
                            }


                            // Category
                            var cat = tbody.ParentNode.ParentNode.FirstChild.NextSibling.InnerText;
                            {
                                cat = cat.Substring(cat.IndexOf(" ")).Trim().ToCamelCase();
                                extInst["Category"] = cat;
                            }

                            // Number and Params
                            {
                                var row = tbody.LastChild.PreviousSibling;

                                var operands = new JArray();

                                //TODO: implement ImageEncoding
                                if (cat == "ImageEncoding" || cat == "SamplerEncoding")
                                    continue;

                                // Start at 6th column (Number)
                                // After Number there are only params
                                for (int i = 11; i < row.ChildNodes.Count; i++)
                                {
                                    var column = row.ChildNodes[i];
                                    int nr;
                                    var operand = new JObject();

                                    // No empty content
                                    if (column.InnerText == "" || column.InnerText == "\n")
                                        continue;

                                    // Number
                                    else if (int.TryParse(column.InnerText, out nr))
                                        extInst["Number"] = nr;
                                    // Operands
                                    else
                                    {
                                        // LiteralNumber
                                        if (column.InnerText.Contains("Literal Number"))
                                        {
                                            var a = column.InnerText.GetLinkedNameAndType(false, true);
                                            operand["Name"] = a[0];
                                            operand["Type"] = a[1];
                                            operands.Add(operand);
                                        }
                                        // printf -> addionalArguemnts as ID Array
                                        else if (column.InnerText.StartsWith("&lt;id&gt;, &lt;id&gt;,"))
                                        {
                                            operand["Name"] = "AdditionalArguments";
                                            operand["Type"] = "ID[]";
                                        }
                                        // IDs
                                        else
                                        {
                                            operand["Name"] = WebUtility.HtmlDecode(column.InnerText).GetName(false);
                                            operand["Tpe"] = "ID";
                                            operands.Add(operand);
                                        }
                                    }
                                }

                                extInst["Operands"] = operands;
                            }

                            extendedInstructions.Add(extInst);

                        }
                        extCL21["ExtendedInstructions"] = extendedInstructions;
                    }

                    // Enums
                    {
                        var enums = new JArray();

                        foreach (var tbody in rootExt.SelectNodes("//tbody"))
                        {
                            var enu = new JObject();

                            // Enums ID
                            if (tbody.ParentNode.ParentNode.FirstChild.NextSibling.Id ==
                                "_a_id_imageformatenc_a_image_format_encoding")
                            {
                                var values = new JArray();

                                Debug.Assert(tbody.ParentNode.ChildNodes.Count > 5);

                                enu["Name"] = tbody.ParentNode.ChildNodes[5].InnerText.Trim();

                                // Values
                                foreach (var tr in tbody.ChildNodes)
                                {
                                    var val = new JObject();
                                    if (tr.Name == "tr")
                                    {
                                        Debug.Assert(tr.ChildNodes.Count > 3);

                                        val["Value"] = int.Parse(tr.ChildNodes[1].InnerText);
                                        val["Name"] = tr.ChildNodes[3].InnerText;

                                        values.Add(val);
                                    }
                                }

                                enu["Values"] = values;

                                enums.Add(enu);

                            }
                        }

                        extCL21["Enums"] = enums;
                    }

                    extJson.Add(extCL21);

                }

                #endregion


                specJson["Extensions"] = extJson;

            }

            #endregion


            // save json
            Console.WriteLine("Writing result json");
            File.WriteAllText(jsonFile, specJson.ToString(Formatting.Indented));
        }
    }
}
