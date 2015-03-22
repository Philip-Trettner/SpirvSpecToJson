using System;
using System.Collections.Generic;
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

            // op code
            {
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
                    if (node.Id != node.NextSibling?.InnerHtml)
                        continue;

                    var opcode = node.Id;

                    Console.WriteLine("  - " + opcode);
                }
            }

            // save json
            Console.WriteLine("Writing result json");
            File.WriteAllText(jsonFile, specJson.ToString(Formatting.Indented));
        }
    }
}
