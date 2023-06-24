using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Genizah
{
    public partial class CensorSettingsDialog : Form
    {
        public CensorSettingsDialog()
        {
            InitializeComponent();

            // Populate options for all combo boxes and restore user setting
            for (int i = 0; i < NameInfo.names.Length; i++)
            {
                this.comboBoxes[i].Items.AddRange(NameInfo.names[i].ReplacementOptions);
                this.comboBoxes[i].Text = NameInfo.names[i].getSelectedReplacement();
            }
        }

        private void CensorSettingsDialog_FormClosed(object sender, FormClosedEventArgs e)
        {
            // Save all user settings
            for (int i = 0; i < NameInfo.names.Length; i++)
            {
                NameInfo.names[i].setSelectedReplacement(comboBoxes[i].Text);
            }
            Properties.Settings.Default.Save();
        }

        private void OkButton_Click(object sender, System.EventArgs e)
        {
            this.Close();
        }

    }
}
