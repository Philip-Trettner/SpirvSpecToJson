using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpirvSpecToJson
{
    class Program
    {
        static void Main(string[] args)
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

            // metadata
            {
                var metaJson = new JObject();

                metaJson["Title"] = root.SelectSingleNode("//div[@id='header']/h1").InnerText;
                metaJson["Author"] = root.SelectSingleNode("//span[@id='author']").InnerText;
                metaJson["Revnumber"] = root.SelectSingleNode("//span[@id='revnumber']").InnerText;
                metaJson["LastUpdate"] = root.SelectSingleNode("//div[@id='footer-text']").LastChild.InnerText.Trim();

                specJson["Metadata"] = metaJson;
            }

            // op code
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

                        for (var i = 2; i < tds.Length; ++i)
                        {
                            var td = tds[i];
                            var text = td.InnerText;

                            if (text == "Result <id>")
                            {
                                // TODO
                            }

                            // TODO
                        }

                        opJson["WordCount"] = wc;
                        opJson["WordCountFix"] = int.Parse(wc.Replace(" + variable", "").Trim());
                        opJson["OpCode"] = opcodeNr;

                        opJson["HasVariableWordCount"] = isVariableWC;
                        opJson["HasResult"] = hasResult;
                        opJson["HasResultType"] = hasResultType;
                    }

                    opcodeJson.Add(opJson);
                }

                specJson["OpCodes"] = opcodeJson;
            }

            // save json
            Console.WriteLine("Writing result json");
            File.WriteAllText(jsonFile, specJson.ToString(Formatting.Indented));
        }
    }
}
