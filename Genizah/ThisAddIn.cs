using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Genizah.Results;
using Microsoft.Office.Interop.Word;
using Microsoft.Office.Tools;
using static System.Net.Mime.MediaTypeNames;
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
            resultsPane = this.CustomTaskPanes.Add(resultsControl, "תוצאות");
            resultsPane.VisibleChanged += HandlePaneVisibleChanged;
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
            foreach (var result in ResultsList)
            {
                result.Unhighlight();
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
                if (name.FindPattern.Match(text).Success)
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
            Application.UndoRecord.StartCustomRecord("צנזור שמות לגניזה");
            Application.ScreenUpdating = false;
            try { 
                foreach (var name in NameInfo.names)
                {
                    ReplaceName(doc, name);
                }

                resultsControl.ShowSearchResults(this.ResultsList);
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

                // A bit of a hack:
                // Word has hidden characters that handle formatting, links etc. These characters are skipped in the string returned by range.Text.
                // Because of this, the range indices don't neccisarily match the range indices (ie. range.Text.Length != range.End).
                // To work around this, we use range.Characters, whose indices match range.Text, to convert text indices to range indices.
                
                // Workaround for tables: Word inserts a special character between table cells, which is actually two unicode characters long.
                string text = range.Text.Replace("\r\a", "\r");

                // Find matches with Regular Expression
                Match match = name.FindPattern.Match(text);
                if (!match.Success) return;

                var chars = range.Characters;
                var matchRange = doc.Range(chars[match.Index+1].Start, chars[match.Index + match.Length].End);

                SearchResult result = new SearchResult(matchRange, name);
                this.ResultsList.Add(result);
            }
        }

        private void HandlePaneVisibleChanged(object sender, EventArgs e)
        {
            foreach (var result in this.ResultsList)
            {
                if (this.resultsPane.Visible)
                    result.Highlight();
                else
                    result.Unhighlight();
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
