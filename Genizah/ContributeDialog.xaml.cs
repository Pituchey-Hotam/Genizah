using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Genizah.Core;

namespace Genizah
{
    /// <summary>
    /// What sending the review's decisions would send, record by record, each of which the user
    /// can leave out.
    /// </summary>
    public partial class ContributeDialog : Window
    {
        private readonly Row[] records;

        public ContributeDialog(IEnumerable<ContributionRecord> offer)
        {
            InitializeComponent();
            records = offer.Select(r => new Row(r)).ToArray();
            rows.ItemsSource = records;
        }

        /// <summary>The records left checked, once the user sends.</summary>
        public List<ContributionRecord> Chosen => records.Where(r => r.Include).Select(r => r.Record).ToList();

        /// <summary>Shows the dialog over Word's active window; true if the user chose to send.</summary>
        public bool? ShowOver(Microsoft.Office.Interop.Word.Application word)
        {
            try
            {
                new WindowInteropHelper(this).Owner = new IntPtr(word.ActiveWindow.Hwnd);
            }
            catch (COMException)
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            return ShowDialog();
        }

        private void Send_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void CheckBox_Loaded(object sender, RoutedEventArgs e) =>
            CensorSettingsDialog.UnmirrorCheckMark((System.Windows.Controls.CheckBox)sender);

        private class Row
        {
            public Row(ContributionRecord record) => Record = record;

            public ContributionRecord Record { get; }
            public bool Include { get; set; } = true;
            public string Left => Record.Left;
            public string Target => Record.Target;
            public string Right => Record.Right;

            public string Decision => (Record.Label == "divine" ? "ההכרעה שלך: שם קודש" : "ההכרעה שלך: אינו שם קודש")
                + (Record.Kind == "override" ? " (בניגוד למודל)" : "");
        }
    }
}
