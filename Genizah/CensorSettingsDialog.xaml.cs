using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Genizah
{
    /// <summary>
    /// The add-in's settings: the replacement each name is censored to, and whether to update.
    /// </summary>
    public partial class CensorSettingsDialog : Window
    {
        private readonly Row[] names = NameInfo.names.Select(n => new Row(n)).ToArray();

        public CensorSettingsDialog()
        {
            InitializeComponent();
            rows.ItemsSource = names;
            checkForUpdates.IsChecked = Properties.Settings.Default.check_for_updates;
        }

        /// <summary>
        /// Shows the dialog over Word's active window, and saves the choices if the user confirms.
        /// </summary>
        public static void ShowOver(Microsoft.Office.Interop.Word.Application word)
        {
            var dialog = new CensorSettingsDialog();
            try
            {
                new WindowInteropHelper(dialog).Owner = new IntPtr(word.ActiveWindow.Hwnd);
            }
            catch (COMException)
            {
                // No document window to center over: the dialog opens in the screen's center.
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            dialog.ShowDialog();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            foreach (Row row in names)
                row.Name.setSelectedReplacement(row.Replacement.Trim());
            Properties.Settings.Default.check_for_updates = checkForUpdates.IsChecked == true;
            Properties.Settings.Default.Save();
            DialogResult = true;
        }

        private void Defaults_Click(object sender, RoutedEventArgs e)
        {
            foreach (Row row in names)
                row.Replacement = row.Options[0];
        }

        private class Row : INotifyPropertyChanged
        {
            private string replacement;

            public Row(NameInfo name)
            {
                Name = name;
                replacement = name.getSelectedReplacement();
            }

            public NameInfo Name { get; }
            public string DisplayName => Name.DisplayName;
            public string[] Options => Name.ReplacementOptions;

            public string Replacement
            {
                get { return replacement; }
                set
                {
                    if (replacement == value) return;
                    replacement = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Replacement)));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }
    }
}
