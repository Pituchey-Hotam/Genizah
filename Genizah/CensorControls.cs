using Microsoft.Office.Tools.Ribbon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Genizah
{
    public partial class CensorControls
    {
        private void CensorControls_Load(object sender, RibbonUIEventArgs e)
        {

        }

        private void CensorNamesBtn_Click(object sender, RibbonControlEventArgs e)
        {
            var window = (Microsoft.Office.Interop.Word.Window)e.Control.Context;
            Globals.ThisAddIn.ShowReview(window);
        }

        private void GenizahGroup_DialogLauncherClick(object sender, RibbonControlEventArgs e)
        {
            CensorSettingsDialog.ShowOver(Globals.ThisAddIn.Application);
        }
    }
}
