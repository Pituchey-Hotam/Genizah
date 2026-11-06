using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Genizah.Core;

namespace Genizah
{
    /// <summary>
    /// The add-in's settings: replacements, print checks, automatic updates and contributions.
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
            offerContributions.IsChecked = AddInSettings.Current.OfferContributions;
            offerContributions.Loaded += (s, e) => UnmirrorCheckMark(offerContributions);
            // Until there's a server to send to, there's nothing to offer.
            contributions.Visibility = Contributing.Endpoint == null ? Visibility.Collapsed : Visibility.Visible;
            deleteContributions.Visibility = string.IsNullOrEmpty(AddInSettings.Current.InstallId) ? Visibility.Collapsed : Visibility.Visible;
            updateStatus.Text = UpdateStatus();
            updateStatus.ToolTip = "פרטים: " + Updater.LogPath;
        }

        /// <summary>The running version, and what the last session's update check found.</summary>
        private static string UpdateStatus()
        {
            Version running = Updater.RunningVersion;
            if (!Updater.IsInstalled) return $"גרסה {running} (גרסת פיתוח, אינה מתעדכנת).";
            Version next = Updater.NextStartVersion;
            if (next != null) return $"גרסה {running}. גרסה {next} הותקנה ותפעל מההפעלה הבאה של Word.";

            UpdateCheck last = UpdateLog.LastCheck(Updater.LogPath);
            if (last == null) return $"גרסה {running}. עדיין לא נבדקו עדכונים.";
            string when = last.Time.ToString("d.M.yyyy HH:mm", CultureInfo.InvariantCulture);
            string found = last.Outcome == UpdateOutcome.Failed
                ? "הבדיקה נכשלה, ותתבצע שוב בהפעלה הבאה של Word."
                : "זו הגרסה העדכנית.";
            return $"גרסה {running}. בדיקה אחרונה: {when}. {found}";
        }

        /// <summary>
        /// Right-to-left mirrors the whole check box, its check mark included. Its glyph alone goes
        /// back to left-to-right, which undoes the mirroring; the box stays on the text's right.
        /// </summary>
        internal static void UnmirrorCheckMark(System.Windows.Controls.CheckBox box)
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
            AddInSettings.Current.OfferContributions = offerContributions.IsChecked == true;
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

        private async void DeleteContributions_Click(object sender, RoutedEventArgs e)
        {
            if (Ask("למחוק מהשרת את כל ההכרעות ששלחת מהמחשב הזה?", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            deleteContributions.IsEnabled = false;
            try
            {
                ContributionDeletion deleted = await Contributing.DeleteAll();
                deleteContributions.Visibility = Visibility.Collapsed;
                Ask(deleted.Records == 0 ? "לא נמצאו הכרעות למחיקה." : $"נמחקו {deleted.Records} הכרעות.",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) when (ex is ContributionException || ex is System.Net.Http.HttpRequestException
                                       || ex is System.Threading.Tasks.TaskCanceledException || ex is IOException)
            {
                Ask("המחיקה נכשלה: " + ex.Message, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                deleteContributions.IsEnabled = true;
            }
        }

        private MessageBoxResult Ask(string text, MessageBoxButton buttons, MessageBoxImage image) =>
            MessageBox.Show(this, text, Title, buttons, image, MessageBoxResult.None,
                            MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

        private void About_Click(object sender, RoutedEventArgs e)
        {
            new AboutDialog { Owner = this }.ShowDialog();
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
