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
            Globals.ThisAddIn.CensorNames(Globals.ThisAddIn.Application.ActiveDocument);
        }

        private void GenizahGroup_DialogLauncherClick(object sender, RibbonControlEventArgs e)
        {
            CensorSettingsDialog dialog = new CensorSettingsDialog();
            dialog.ShowDialog();
        }
    }
}
