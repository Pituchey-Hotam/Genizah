using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Word = Microsoft.Office.Interop.Word;
using Office = Microsoft.Office.Core;
using Microsoft.Office.Tools.Word;
using System.Windows.Forms;

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

        void Application_DocumentBeforePrint(Word.Document doc, ref bool cancel)
        {
            DialogResult dialogResult = MessageBox.Show("האם תרצה לצנזר שמות ה' לפני הדפסה?",
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

        public void CensorNames(Word.Document doc)
        {
            ReplaceInDocument(doc);
        }

        private void ReplaceInDocument(Word.Document doc)
        {
            Word.Range range = doc.Range();
            Word.Find findObject = range.Find;

            findObject.ClearFormatting();
            findObject.Text = "אלהים";
            findObject.Replacement.ClearFormatting();
            findObject.Replacement.Text = "-להים";

            findObject.Execute(ref missing, ref missing, ref missing, ref missing, ref missing,
                ref missing, ref missing, ref missing, ref missing, ref missing,
                Word.WdReplace.wdReplaceAll, ref missing, ref missing, ref missing, ref missing);
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
