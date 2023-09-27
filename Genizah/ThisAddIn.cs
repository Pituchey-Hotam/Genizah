using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Word = Microsoft.Office.Interop.Word;
using System.Windows.Forms;
using Microsoft.Office.Interop.Word;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Net.Http;

namespace Genizah
{
    public partial class ThisAddIn
    {
        private void ThisAddIn_Startup(object sender, EventArgs e)
        {
            Application.DocumentBeforePrint += new Word.ApplicationEvents4_DocumentBeforePrintEventHandler(Application_DocumentBeforePrint);
        }

        private void ThisAddIn_Shutdown(object sender, EventArgs e)
        {
        }

        /// <summary>
        /// Handles a print event, warning the user of any names and offering to censor
        /// </summary>
        void Application_DocumentBeforePrint(Word.Document doc, ref bool cancel)
        {
            // Do not warn if the document contains no names
            if (FindNames(doc))
            {
                DialogResult dialogResult = MessageBox.Show("מסמך זה מכיל שמות שאינם נמחקים וטעון גניזה. האם תרצה לצנזר שמות ה' לפני הדפסה?",
                                            "צנזור גניזה",
                                            MessageBoxButtons.YesNoCancel);
                if (dialogResult == DialogResult.Yes)
                {
                    CensorNames(doc);
                }
                else if (dialogResult == DialogResult.Cancel)
                {
                    cancel = true;
                }
            }
        }

        /// <summary>
        /// Searches for holy names in a document
        /// </summary>
        /// <returns>true if the document contains any names</returns>
        public bool FindNames(Word.Document doc)
        {
            foreach (var name in NameInfo.names)
            {
                if (FindReplaceInDocument(doc, name.FindPattern, "", Word.WdReplace.wdReplaceNone))
                {
                    return true;
                }
            }
            return false;
        }

        // A constant to represent the value of a noun morph code.
        public const long BASEFORM_POS_NOUN = 0x60000;

        /// <summary>
        /// Censors names in a document, replacing them with the user preferred replacement
        /// </summary>
        public void CensorNames(Word.Document doc)
        {
            String contents = doc.Content.Text;

            var names = NameInfo.names.Select(x => x.FindPattern);
            var a = doc.Content.WordOpenXML;

            var replacements = new List<(int index, NameInfo name)>();
            int k = 0;

            // Sentences and words indexex are starting from index 1.
            for (int i = 1; i < doc.Sentences.Count + 1; i++)
            {
                var foundNames = NameInfo.names.Where(name => FindReplaceInRange(doc.Sentences[i], name.FindPattern, "", WdReplace.wdReplaceNone));
                if (foundNames.Any())
                {
                    var chk = CheckWordAsync(doc.Sentences[i].Text.Trim()).Result; // CurCheckWordAsync(doc.Sentences[i].Text.Trim()); 
                    for (int j = 1; j < doc.Sentences[i].Words.Count + 1; j++)
                    {
                        foreach (var name in NameInfo.names)
                        {
                            // Using EndsWith to avoid problems like confustion of "אלקים" becuase it has to letters "אל".
                            if (doc.Sentences[i].Words[j].Text.Trim().EndsWith(name.FindPattern) && !replacements.Any(val => val.Item1 == j) &&
                                (chk[j - 1].Morph & BASEFORM_POS_NOUN) != 0)
                            {
                                // Used this method becuase of a bug with changed indexes when adding '-' to a word.
                                replacements.Add((j, name));
                            }
                        }
                    }

                    foreach (var replacing in replacements)
                    {
                        FindReplaceInRange(doc.Sentences[i].Words[replacing.index + 2 * k],
                            replacing.Item2.FindPattern, replacing.Item2.getSelectedReplacement());

                        // Added becuase of a bug with changed indexes when adding '-' to a word.
                        if (replacing.Item2.getSelectedReplacement().Contains('-'))
                            k++;
                    }

                    replacements.Clear();
                    k = 0;
                }
            }
        }

        private bool FindReplaceInDocument(Word.Document doc, string target, string replacement, Word.WdReplace replaceMode = Word.WdReplace.wdReplaceAll)
        {
            Word.Range range = doc.Content;
            Word.Find findObject = range.Find;

            findObject.ClearFormatting();
            findObject.Replacement.ClearFormatting();

            findObject.Execute(
                FindText: target,
                MatchCase: true,
                MatchWholeWord: false,
                MatchWildcards: false,
                MatchSoundsLike: false,
                MatchAllWordForms: false,
                Forward: true,
                Wrap: WdFindWrap.wdFindStop,
                Format: ref missing,
                ReplaceWith: replacement,
                Replace: replaceMode,
                MatchKashida: false,
                MatchDiacritics: false,
                MatchAlefHamza: false,
                MatchControl: false);

            return findObject.Found;
        }

        private bool FindReplaceInRange(Range range, string target, string replacement, Word.WdReplace replaceMode = Word.WdReplace.wdReplaceAll)
        {
            Word.Find findObject = range.Find;

            findObject.ClearFormatting();
            findObject.Replacement.ClearFormatting();

            findObject.Execute(
                FindText: target,
                MatchCase: true,
                MatchWholeWord: false,
                MatchWildcards: false,
                MatchSoundsLike: false,
                MatchAllWordForms: false,
                Forward: true,
                Wrap: WdFindWrap.wdFindStop,
                Format: ref missing,
                ReplaceWith: replacement,
                Replace: replaceMode,
                MatchKashida: false,
                MatchDiacritics: false,
                MatchAlefHamza: false,
                MatchControl: false);

            return findObject.Found;
        }

        /// <summary>
        /// Receives a string and removes every nikud character in it.
        /// </summary>
        static string RemoveNikud(string input)
        {
            // Define a regular expression pattern to match nikud characters.
            string pattern = "[\u0591-\u05BD\u05BF-\u05C2\u05C4\u05C5\u05C7]";

            // Use Regex.Replace to remove nikud characters.
            string result = Regex.Replace(input, pattern, string.Empty);

            return result;
        }

        /// <summary>
        /// Receives a string represtring a text paragraph, sends it to the Dicta Nakdan API and 
        /// returns a list of WordMorphs (a class of a word and its morph code).
        /// </summary>
        private async Task<List<WordMorph>> CheckWordAsync(string text)
        {
            string responseContent = "";
            string apiUrl = "https://nakdan-4-0.loadbalancer.dicta.org.il/addnikud";
            string apiKey = "#### API-KEY ####";

            HttpClient httpClient = new HttpClient();

            var requestData = new
            {
                task = "nakdan",
                useTokenization = true,
                genre = "modern",
                data = text,
                addmorph = true,
                matchpartial = true,
                keepmetagim = false,
                keepqq = false,
                apiKey = apiKey
            };

            var jsonRequestData = JsonSerializer.Serialize(requestData);

            HttpContent content = new StringContent(jsonRequestData, Encoding.UTF8, "application/json");

            HttpResponseMessage response = await httpClient.PostAsync(apiUrl, content);

            if (response.IsSuccessStatusCode)
            {
                responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine(responseContent);
            }
            else
            {
                // TODO
                return null;
                //responseContent = "Request failed with status code: " + response.StatusCode;
            }

            return ProcessJson(responseContent);


        }

        /// <summary>
        /// Class to represent a word and its morph. for more inforamtion about the morph codes
        /// <see href="https://docs.google.com/document/d/13bGP61lzXkEPc7KyAd1iOg0mgUazvt6ZOp1TALdVl90/">Click Here</see>
        /// </summary>
        public class WordMorph
        {
            public string Word { get; set; }
            public long Morph { get; set; }
        }

        /// <summary>
        /// Receives a string representing a json array, proccesses it and returns a list of WordMorphs (a word and its morph).
        /// </summary>
        public static List<WordMorph> ProcessJson(string json)
        {
            var items = new List<WordMorph>();

            // Parse the JSON string
            var jsonDocument = JsonDocument.Parse(json);

            // Navigate the JSON structure and filter the list
            var data = jsonDocument.RootElement.GetProperty("data")
                .EnumerateArray()
                .Where(item => !item.GetProperty("nakdan").GetProperty("sep").GetBoolean())
                .ToList();

            foreach (var item in data)
            {
                var nakdan = item.GetProperty("nakdan");
                var options = nakdan.GetProperty("options").EnumerateArray().FirstOrDefault();

                string word = nakdan.GetProperty("word").GetString();
                long morph = long.Parse(options.GetProperty("morph").GetString());

                items.Add(new WordMorph { Word = word, Morph = morph });
            }

            return items;
        }


        #region VSTO generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InternalStartup()
        {
            this.Startup += new System.EventHandler(ThisAddIn_Startup);
            this.Shutdown += new System.EventHandler(ThisAddIn_Shutdown);
        }

        #endregion
    }
}
