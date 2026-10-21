using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Navigation;
using Genizah.Scanning;
using Word = Microsoft.Office.Interop.Word;

namespace Genizah
{
    /// <summary>
    /// What the add-in is, its halachic disclaimer, its license and its credits.
    /// </summary>
    public partial class AboutDialog : Window
    {
        public AboutDialog()
        {
            InitializeComponent();
            version.Text = $"גרסה {Updater.RunningVersion}";
        }

        /// <summary>
        /// Opens the dialog over <paramref name="window"/> the first time the user opens the add-in,
        /// so its disclaimer is read before the first scan.
        /// </summary>
        internal static void ShowOnFirstUse(Word.Window window)
        {
            if (AddInSettings.Current.AboutShown) return;
            var dialog = new AboutDialog();
            new WindowInteropHelper(dialog).Owner = new IntPtr(window.Hwnd);
            dialog.ShowDialog();
            AddInSettings.Current.AboutShown = true;
            try
            {
                AddInSettings.Save();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // It stays shown for this session, and opens again in the next.
            }
        }

        /// <summary>Opens the NOTICE file shipped beside the add-in.</summary>
        private void Notice_Click(object sender, RoutedEventArgs e)
        {
            string path = Path.Combine(ModelLoader.AddInDir, "NOTICE");
            try
            {
                Process.Start("notepad.exe", "\"" + path + "\"");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                MessageBox.Show(this, "לא ניתן לפתוח את " + path + ": " + ex.Message, Title,
                                MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK,
                                MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            }
        }

        private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
