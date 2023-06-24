using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Word = Microsoft.Office.Interop.Word;
using Office = Microsoft.Office.Core;
using Microsoft.Office.Tools.Word;
using System.Windows.Forms;
using Microsoft.Office.Interop.Word;

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

        /// <summary>
        /// Censors names in a document, replacing them with the user preferred replacement
        /// </summary>
        public void CensorNames(Word.Document doc)
        {
            foreach (var name in NameInfo.names)
            {
                FindReplaceInDocument(doc, name.FindPattern, name.getSelectedReplacement());
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
