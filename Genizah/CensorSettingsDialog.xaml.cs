using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Genizah
{
    /// <summary>
    /// The add-in's settings: replacements, print checks and automatic updates.
    /// </summary>
    public partial class CensorSettingsDialog : Window
    {
        private readonly Row[] names = NameInfo.names.Select(n => new Row(n)).ToArray();

        public CensorSettingsDialog()
        {
            InitializeComponent();
            rows.ItemsSource = names;
            checkForUpdates.IsChecked = AddInSettings.Current.CheckForUpdates;
            checkBeforePrinting.IsChecked = AddInSettings.Current.CheckBeforePrinting;
            checkBeforePrinting.Loaded += (s, e) => UnmirrorCheckMark(checkBeforePrinting);
            checkForUpdates.Loaded += (s, e) => UnmirrorCheckMark(checkForUpdates);
        }

        /// <summary>
        /// Right-to-left mirrors the whole check box, its check mark included. Its glyph alone goes
        /// back to left-to-right, which undoes the mirroring; the box stays on the text's right.
        /// </summary>
        private static void UnmirrorCheckMark(System.Windows.Controls.CheckBox box)
        {
            box.ApplyTemplate();
            // The glyph's grid in WPF's own templates; another theme may have none.
            if (box.Template?.FindName("markGrid", box) is FrameworkElement mark)
                mark.FlowDirection = FlowDirection.LeftToRight;
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
            AddInSettings.Current.CheckForUpdates = checkForUpdates.IsChecked == true;
            AddInSettings.Current.CheckBeforePrinting = checkBeforePrinting.IsChecked == true;
            try
            {
                AddInSettings.Save();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // The choices still apply until Word closes.
                MessageBox.Show(this, "לא ניתן לשמור את ההגדרות: " + ex.Message, Title,
                                MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK,
                                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            }
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
