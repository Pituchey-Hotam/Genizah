using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
            if (ContainsNames(doc))
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
        public bool ContainsNames(Word.Document doc)
        {
            string text = doc.Content.Text;
            foreach (var name in NameInfo.names)
            {
                if (name.FindPattern.Match(doc.Content.Text).Success)
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
            Application.UndoRecord.StartCustomRecord("צנזור שמות לגניזה");
            Application.ScreenUpdating = false;
            try { 
                foreach (var name in NameInfo.names)
                {
                    ReplaceName(doc, name);
                }

                resultsControl.UpdateSearchResults(this.ResultsList);
                resultsPane.Visible = true;
            }
            finally
            {
                Application.ScreenUpdating = true;
                Application.UndoRecord.EndCustomRecord();
            }
}

        private void ReplaceName(Document doc, NameInfo name)
        {
            while (true)
            {
                // Fetch document contents
                Word.Range range = doc.Content;
                string text = range.Text;

                // Find matches with Regular Expression
                Match match = name.FindPattern.Match(text);
                if (!match.Success) return;

                // A bit of a hack:
                // Word has hidden characters that handle formatting, links etc. These characters are skipped in the string returned by range.Text.
                // Because of this, the range indices don't neccisarily match the range indices (ie. range.Text.Length != range.End).
                // To work around this, we use range.Characters, whose indices match range.Text, to convert text indices to range indices.
                var chars = range.Characters;
                var matchRange = doc.Range(chars[match.Index+1].Start, chars[match.Index + match.Length].End);

                var originalText = matchRange.Text;
                var originalHighlight = matchRange.HighlightColorIndex;
                matchRange.Text = name.getSelectedReplacement();
                var bookmarkId = '_' + originalText + Guid.NewGuid().ToString().Split('-').First();
                matchRange.HighlightColorIndex = WdColorIndex.wdYellow;
                Word.Bookmark bookmark = this.Application.ActiveDocument.Bookmarks.Add(bookmarkId, matchRange);
                SearchResult result = new SearchResult()
                {
                    Bookmark = bookmark,
                    OriginalText = originalText,
                    OriginalHighlight = originalHighlight,
                    ReplacementText = name.getSelectedReplacement(),
                };
                this.ResultsList.Add(result);
            }
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
