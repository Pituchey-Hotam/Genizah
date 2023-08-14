using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Genizah.Results;
using Microsoft.Office.Interop.Word;
using Microsoft.Office.Tools;
using Word = Microsoft.Office.Interop.Word;

namespace Genizah
{
    public partial class ThisAddIn
    {
        public List<SearchResult> ResultsList = new List<SearchResult>();
        public ResultsControl resultsControl = new ResultsControl();
        public CustomTaskPane resultsPane;
        private void ThisAddIn_Startup(object sender, EventArgs e)
        {
            Application.DocumentBeforePrint += new Word.ApplicationEvents4_DocumentBeforePrintEventHandler(Application_DocumentBeforePrint);
            resultsPane = this.CustomTaskPanes.Add(resultsControl, "תוצאות");
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

            // Remove Results HighLights
            if (ResultsList.Count > 0)
            {
                foreach (var result in ResultsList)
                {
                    result.Bookmark.Range.HighlightColorIndex = 0;
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

        /// <summary>
        /// Censors names in a document, replacing them with the user preferred replacement
        /// </summary>
        public void CensorNames(Word.Document doc)
        {
            this.ResultsList = new List<SearchResult>();
            foreach (var name in NameInfo.names)
            {
                FindReplaceInDocument(doc, name.FindPattern, name.getSelectedReplacement());
            }
            resultsControl.UpdateSearchResults(this.ResultsList);
            resultsPane.Visible = true;
        }

        private bool FindReplaceInDocument(Word.Document doc, string target, string replacement, Word.WdReplace replaceMode = Word.WdReplace.wdReplaceAll)
        {
            Word.Range range = doc.Content;
            Word.Find findObject = range.Find;

            findObject.ClearFormatting();
            findObject.Replacement.ClearFormatting();
            findObject.Text = target;
            findObject.Forward = true;
            findObject.Wrap = WdFindWrap.wdFindStop;
            findObject.MatchWholeWord = false;
            findObject.MatchWildcards = false;
            findObject.MatchSoundsLike = false;
            findObject.MatchAllWordForms = false;
            findObject.MatchDiacritics = false;
            findObject.MatchControl = false;
            findObject.IgnorePunct = true;

            if (replaceMode == WdReplace.wdReplaceNone)
            {
                findObject.Execute(Format: ref missing, Replace: replaceMode);
            }
            else
            {
                while (findObject.Execute())
                {
                    var originalText = range.Text;
                    range.Text = replacement;
                    var bookmarkId = originalText + Guid.NewGuid().ToString().Split('-').First();
                    Word.Bookmark bookmark = this.Application.ActiveDocument.Bookmarks.Add(bookmarkId, range);
                    range.HighlightColorIndex = WdColorIndex.wdYellow;
                    SearchResult result = new SearchResult()
                    {
                        Bookmark = bookmark,
                        rangeStart = range.Start,
                        rangeEnd = range.End,
                        OriginalText = originalText,
                        ReplacementText = replacement
                    };
                    this.ResultsList.Add(result);
                }
            }

            return findObject.Found;
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
