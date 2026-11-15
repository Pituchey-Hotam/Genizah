using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Genizah.Core;
using Genizah.Scanning;

namespace Genizah.Results
{
    /// <summary>
    /// The results pane's content: one collapsible group per verdict, a one-line row per result,
    /// and the selected result's context and actions below the list. Selecting a row selects its
    /// name in the document.
    /// </summary>
    public partial class ResultsView : UserControl
    {
        /// <summary>What the list shows: each group's heading, followed by its rows while it's expanded.</summary>
        private ObservableCollection<object> items = new ObservableCollection<object>();
        private readonly Dictionary<GroupHeader, List<ResultItem>> groupRows = new Dictionary<GroupHeader, List<ResultItem>>();
        private List<SearchResult> results = new List<SearchResult>();
        private string notice;
        private string summaryText = "";

        // The offer to send the review's decisions (Contributing), from this scan's results.
        private Thresholds thresholds;
        /// <summary>Keys of the decisions the user chose not to send, until the next scan.</summary>
        private readonly HashSet<string> declined = new HashSet<string>();
        private bool sending;
        private ContributionBatch failedBatch;
        private string failure;
        private string sentMessage;
        private bool contributionOfferAvailable;
        /// <summary>Counts scans, so a send that finishes after a new one doesn't report on it.</summary>
        private int generation;

        /// <summary>Raised after the user censors or leaves a name from the pane.</summary>
        public event EventHandler ResultChanged;
        public event EventHandler ReviewRequested;
        public event EventHandler ScanRequested;

        public ResultsView()
        {
            InitializeComponent();
            list.ItemsSource = items;
        }

        private void Scan_Click(object sender, RoutedEventArgs e) => ScanRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>Shows a message in place of results: what went wrong, say.</summary>
        public void ShowMessage(string message)
        {
            ClearResults();
            ShowProgressPanel(false);
            summary.Text = summaryText = message;
            summary.Visibility = Visibility.Visible;
            summaryCounts.Visibility = summaryNotice.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Shows a scan's progress in place of results: its stage, and how far it is through it.
        /// A <paramref name="total"/> of 0 means the stage can't count, and the bar just moves.
        /// </summary>
        public void ShowProgress(string stage, int done, int total)
        {
            if (progressPanel.Visibility != Visibility.Visible)
            {
                ClearResults();
                ShowProgressPanel(true);
            }
            progressStage.Text = stage;
            progressBar.IsIndeterminate = total == 0;
            progressBar.Maximum = Math.Max(total, 1);
            progressBar.Value = done;
            progressCount.Text = total == 0 ? "" : $"{done} מתוך {total}";
        }

        private void ClearResults()
        {
            results = new List<SearchResult>();
            notice = null;
            groupRows.Clear();
            items.Clear();
            ResetContribution(null);
        }

        private void ResetContribution(Thresholds scanned)
        {
            thresholds = scanned;
            declined.Clear();
            failedBatch = null;
            sentMessage = null;
            generation++;
            UpdateContribution();
        }

        private void ShowProgressPanel(bool show)
        {
            progressPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            reviewHeader.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            scanFooter.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            // Stops the moving bar's animation along with it.
            if (!show) progressBar.IsIndeterminate = false;
        }

        /// <summary>
        /// Lists the results in groups: the ones the model asked about, then those it censored, then
        /// those it left, each in document order. Only the first group starts expanded.
        /// <paramref name="notice"/> is shown above them. The decisions can be offered for sending
        /// only with the <paramref name="thresholds"/> the results were decided by.
        /// </summary>
        public void ShowSearchResults(List<SearchResult> results, string notice, Thresholds thresholds)
        {
            this.results = results;
            this.notice = notice;
            ResetContribution(thresholds);
            groupRows.Clear();
            var shown = new List<object>();
            foreach (Verdict verdict in new[] { Verdict.Ask, Verdict.Censor, Verdict.Leave })
            {
                List<ResultItem> rows = results.Where(r => r.Verdict == verdict).Select(r => new ResultItem(r)).ToList();
                if (rows.Count == 0) continue;
                var header = new GroupHeader(verdict, rows.Count, isExpanded: groupRows.Count == 0);
                groupRows[header] = rows;
                shown.Add(header);
                if (header.IsExpanded)
                    shown.AddRange(rows);
            }
            items = new ObservableCollection<object>(shown);
            list.ItemsSource = items;
            ShowProgressPanel(false);
            ShowSummary();
        }

        public void RefreshResults()
        {
            foreach (var rows in groupRows.Values)
                foreach (var row in rows) row.Refresh();
            ShowSummary();
        }

#if DEBUG
        internal void RequestScan() => scanButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        internal string InspectReviewUi()
        {
            var selected = list.SelectedItem as ResultItem;
            return string.Join("|", progressPanel.Visibility == Visibility.Visible ? "progress" : "review",
                scanButton.Content, Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(summaryText)),
                selected == null ? -1 : results.IndexOf(selected.Result),
                string.Join(",", groupRows.Keys.Select(header => header.IsExpanded)),
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(progressStage.Text)));
        }

        internal bool ReviewResult(int index, bool censor)
        {
            details.DataContext = groupRows.Values.SelectMany(rows => rows).First(row => row.Result == results[index]);
            return censor ? Censor() : Leave();
        }

        /// <summary>Selects the result's row as a click does, expanding its group first.</summary>
        internal void SelectResult(int index)
        {
            var group = groupRows.First(pair => pair.Value.Any(row => row.Result == results[index]));
            if (!group.Key.IsExpanded) Toggle(group.Key);
            list.SelectedItem = group.Value.First(row => row.Result == results[index]);
        }
#endif

        private void Toggle(GroupHeader header)
        {
            int at = items.IndexOf(header) + 1;
            List<ResultItem> rows = groupRows[header];
            if (header.IsExpanded)
            {
                for (int i = 0; i < rows.Count; i++)
                    items.RemoveAt(at);
            }
            else
            {
                for (int i = 0; i < rows.Count; i++)
                    items.Insert(at + i, rows[i]);
            }
            header.IsExpanded = !header.IsExpanded;
        }

        private void Header_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Toggle((GroupHeader)((FrameworkElement)sender).DataContext);
        }

        private void List_KeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Key == Key.Enter || e.Key == Key.Space) && list.SelectedItem is GroupHeader header)
            {
                Toggle(header);
                e.Handled = true;
            }
        }

        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (list.SelectedItem is ResultItem item)
            {
                ReviewRequested?.Invoke(this, EventArgs.Empty);
                item.Result.MarkSeen();
                item.Result.Reconcile();
                item.Refresh();
                ShowSummary();
                details.DataContext = item;
                details.Visibility = Visibility.Visible;
                try
                {
                    if (!item.Result.IsStale) item.Result.Range.Select();
                }
                catch (COMException)
                {
                    // The name's text was deleted, or its document closed.
                }
            }
            else
            {
                details.DataContext = null;
                details.Visibility = Visibility.Collapsed;
            }
        }

        private void Censor_Click(object sender, RoutedEventArgs e)
        {
            Censor();
            FocusSelected();
        }

        private void View_FocusChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue) ReviewRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Leave_Click(object sender, RoutedEventArgs e)
        {
            Leave();
            FocusSelected();
        }

        /// <summary>
        /// The shortcuts: צ censors, ה leaves (or undoes a censor), and either moves on to the next
        /// result. By the character typed, so C and L do the same on an English layout.
        /// </summary>
        private void View_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (!(details.DataContext is ResultItem item)) return;
            if (e.Text != "צ" && e.Text != "ה" && e.Text.ToLowerInvariant() != "c" && e.Text.ToLowerInvariant() != "l") return;
            ReviewRequested?.Invoke(this, EventArgs.Empty);
            item.Result.Reconcile();
            item.Refresh();
            ShowSummary();
            bool acted;
            switch (e.Text.ToLowerInvariant())
            {
                case "צ": case "c": acted = item.CanCensor && Censor(); break;
                case "ה": case "l": acted = Leave(); break;
                default: return;
            }
            e.Handled = true;
            if (acted)
                SelectNextResult(item);
            FocusSelected();
        }

        private bool Censor() => Act(r => r.Censor(), "צנזור שם");

        private bool Leave() => Act(r => r.Leave(), "ביטול צנזור שם");

        /// <summary>Selects the result after <paramref name="item"/> in the list, if there is one shown.</summary>
        private void SelectNextResult(ResultItem item)
        {
            for (int i = items.IndexOf(item) + 1; i < items.Count; i++)
            {
                if (items[i] is ResultItem next && !next.Result.IsStale)
                {
                    list.SelectedItem = next;
                    return;
                }
            }
        }

        /// <summary>
        /// Gives the selected row the keyboard focus, so the arrows and shortcuts keep working after
        /// a button click (the clicked button may have just been hidden).
        /// </summary>
        private void FocusSelected()
        {
            if (list.SelectedItem == null) return;
            list.ScrollIntoView(list.SelectedItem);
            list.UpdateLayout();
            (list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as ListBoxItem)?.Focus();
        }

        /// <returns>Whether the action ran; false if Word refused it.</returns>
        private bool Act(Action<SearchResult> action, string undoName)
        {
            if (!(details.DataContext is ResultItem item)) return false;
            ReviewRequested?.Invoke(this, EventArgs.Empty);
            item.Result.Reconcile();
            item.Refresh();
            ShowSummary();
            if (item.Result.IsStale) return false;
            var document = item.Result.Range.Document;
            bool done = false;
            var undo = Globals.ThisAddIn.Application.UndoRecord;
            bool recording = false;
            Globals.ThisAddIn.BeginResultEdit();
            try
            {
                undo.StartCustomRecord(undoName);
                recording = true;
                action(item.Result);
                item.Result.RecordDecision();
                done = true;
            }
            catch (COMException ex)
            {
                System.Windows.Forms.MessageBox.Show("לא ניתן לשנות את השם במסמך: " + ex.Message, "צנזור גניזה");
            }
            finally
            {
                try { if (recording) undo.EndCustomRecord(); }
                finally { Globals.ThisAddIn.EndResultEdit(document); }
            }
            item.Refresh();
            ShowSummary();
            if (done) ResultChanged?.Invoke(this, EventArgs.Empty);
            return done;
        }

        private void ShowSummary()
        {
            string text = results.Count == 0 ? "לא נמצאו שמות." :
                $"ממתינים להכרעה: {results.Count(r => !r.IsStale && r.State == ResultState.Pending)} · " +
                $"צונזרו: {results.Count(r => !r.IsStale && r.State == ResultState.Censored)} · " +
                $"הושארו: {results.Count(r => !r.IsStale && r.State == ResultState.Left)}";
            int stale = results.Count(r => r.IsStale);
            if (stale > 0) text += $" · דרושה סריקה מחדש: {stale}";
            summaryText = notice == null ? text : text + "\n" + notice;
            int pending = results.Count(r => !r.IsStale && r.State == ResultState.Pending);
            var lines = new List<object>
            {
                new { Label = "ממתינים להכרעה", Count = pending, Pending = pending > 0 },
                new { Label = "צונזרו", Count = results.Count(r => !r.IsStale && r.State == ResultState.Censored), Pending = false },
                new { Label = "הושארו", Count = results.Count(r => !r.IsStale && r.State == ResultState.Left), Pending = false }
            };
            if (stale > 0) lines.Add(new { Label = "דרושה סריקה מחדש", Count = stale, Pending = false });
            summaryLines.ItemsSource = lines;
            summaryCounts.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            summary.Text = results.Count == 0 ? "לא נמצאו שמות." : "";
            summary.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            summaryNotice.Text = notice;
            summaryNotice.Visibility = string.IsNullOrEmpty(notice) ? Visibility.Collapsed : Visibility.Visible;
            UpdateContribution();
        }

        /// <summary>
        /// The decisions to offer: eligible decisions not yet sent or declined, even during review.
        /// </summary>
        private List<ContributionRecord> Offer()
        {
            if (thresholds == null || !Contributing.Offered)
                return new List<ContributionRecord>();
            return Contributing.Unsent(results, thresholds).Where(r => !declined.Contains(r.Key)).ToList();
        }

        /// <summary>The explanation the first share link opens, and every later one's tooltip.</summary>
        private const string ShareExplanation =
            "שתף את ההכרעות שלך כדי לשפר את התוסף עבור משתמשים אחרים.\n" +
            "נשלחים רק כמה מילים סביב כל שם, וההכרעה שלך.";

        /// <summary>
        /// Shows the footer's share slot as things stand: sending, failed, offering the decisions
        /// (or, with automatic sharing, sending them), thanking for them, or nothing.
        /// </summary>
        private void UpdateContribution()
        {
            contributionOfferAvailable = false;
            contributePreview.Visibility = Visibility.Collapsed;
            List<ContributionRecord> offer = sending || failedBatch != null ? null : Offer();
            if (offer != null && offer.Count > 0 && AddInSettings.Current.ShareAutomatically)
            {
                // Contribute shows its own progress, and updates the slot again when it's done.
                _ = Contribute(Contributing.NewBatch(offer, thresholds), automatic: true);
                return;
            }
            if (sending)
                ShowContribution(null, "משתף…", null);
            else if (failedBatch != null)
                ShowContribution("נסה שוב", " · השיתוף נכשל", failure);
            else if (offer.Count > 0)
            {
                ShowContribution(
                    offer.Count == 1 ? "דווח טעות אחת" : $"דווח {offer.Count} טעויות",
                    "",
                    ShareExplanation);
                contributePreview.Visibility = Contributing.Explained ? Visibility.Visible : Visibility.Collapsed;
                contributionOfferAvailable = true;
                HighlightContribution();
            }
            else if (sentMessage != null)
                ShowContribution(null, sentMessage, "אפשר לשנות זאת, ולמחוק את מה ששותף, בהגדרות.");
            else
            {
                contributePopup.IsOpen = false;
                contributeSlot.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>The share slot: a <paramref name="link"/> (if any) followed by plain <paramref name="status"/>.</summary>
        private void ShowContribution(string link, string status, string tooltip)
        {
            contributeLinkText.Text = link ?? "";
            contributeLink.IsEnabled = link != null;
            contributeStatus.Text = link == null ? status.TrimStart(' ', '·') : status;
            contributeSlot.ToolTip = tooltip;
            contributeLink.ToolTip = tooltip;
            contributeSlot.Visibility = Visibility.Visible;
        }

        private async void ContributeLink_Click(object sender, RoutedEventArgs e)
        {
            if (failedBatch != null)
            {
                await Contribute(failedBatch);
                return;
            }
            List<ContributionRecord> offer = Offer();
            if (offer.Count == 0) return;
            if (Contributing.Explained)
                await Contribute(Contributing.NewBatch(offer, thresholds));
            else
            {
                OpenContributionIntro();
            }
        }

        private void OpenContributionIntro()
        {
            shareAutomatically.IsChecked = false;
            contributePopup.IsOpen = true;
        }

        private void ContributionSlot_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue) HighlightContribution();
        }

        private void HighlightContribution()
        {
            if (!contributionOfferAvailable || !contributeSlot.IsVisible || Contributing.Explained ||
                AddInSettings.Current.ContributionHintShown) return;

            // Claim the cue before animating, so refreshes and other panes cannot repeat it.
            AddInSettings.Current.ContributionHintShown = true;
            try { AddInSettings.Save(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            if (!SystemParameters.ClientAreaAnimation) return;

            var tint = System.Windows.Media.Color.FromArgb(0, 24, 90, 189);
            var brush = new System.Windows.Media.SolidColorBrush(tint);
            contributeLink.Background = brush;
            var animation = new System.Windows.Media.Animation.ColorAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new System.Windows.Media.Animation.LinearColorKeyFrame(tint,
                System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new System.Windows.Media.Animation.LinearColorKeyFrame(
                System.Windows.Media.Color.FromArgb(36, 24, 90, 189),
                System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5))));
            animation.KeyFrames.Add(new System.Windows.Media.Animation.LinearColorKeyFrame(
                System.Windows.Media.Color.FromArgb(36, 24, 90, 189),
                System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.5))));
            animation.KeyFrames.Add(new System.Windows.Media.Animation.LinearColorKeyFrame(tint,
                System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.5))));
            animation.FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop;
            brush.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, animation);
        }

        private async void ContributePopupSend_Click(object sender, RoutedEventArgs e)
        {
            contributePopup.IsOpen = false;
            List<ContributionRecord> offer = Offer();
            if (offer.Count == 0) return;
            KeepAutomaticChoice();
            await Contribute(Contributing.NewBatch(offer, thresholds));
        }

        private void ContributePopupClose_Click(object sender, RoutedEventArgs e) => contributePopup.IsOpen = false;

        /// <summary>Shows exactly what would be sent, record by record, and sends the ones the user keeps.</summary>
        private async void ContributeShow_Click(object sender, RoutedEventArgs e)
        {
            contributePopup.IsOpen = false;
            List<ContributionRecord> offer = Offer();
            if (offer.Count == 0) return;
            var dialog = new ContributeDialog(offer);
            if (dialog.ShowOver(Globals.ThisAddIn.Application) != true) return;
            List<ContributionRecord> chosen = dialog.Chosen;
            declined.UnionWith(offer.Except(chosen).Select(r => r.Key));
            if (chosen.Count > 0)
            {
                KeepAutomaticChoice();
                await Contribute(Contributing.NewBatch(chosen, thresholds));
            }
            else
                UpdateContribution();
        }

        /// <summary>Saves the popup's "share automatically" box, if the user ticked it.</summary>
        private void KeepAutomaticChoice()
        {
            if (shareAutomatically.IsChecked != true) return;
            AddInSettings.Current.ShareAutomatically = true;
            try { AddInSettings.Save(); }
            // Then it lasts for this session only.
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }

        private void CheckBox_Loaded(object sender, RoutedEventArgs e) =>
            CensorSettingsDialog.UnmirrorCheckMark((CheckBox)sender);

        /// <summary>Sends a batch; on failure, keeps it to retry as it is, so the server can recognize a resend.</summary>
        private async Task Contribute(ContributionBatch batch, bool automatic = false)
        {
            int scan = generation;
            sending = true;
            failedBatch = null;
            UpdateContribution();
            try
            {
                await Contributing.Send(batch);
                if (scan == generation)
                {
                    int n = batch.Records.Count;
                    sentMessage = automatic
                        ? (n == 1 ? "✓ ההכרעה שותפה אוטומטית" : $"✓ {n} הכרעות שותפו אוטומטית")
                        : (n == 1 ? "✓ ההכרעה שותפה. תודה!" : $"✓ {n} הכרעות שותפו. תודה!");
                }
            }
            catch (ContributionException ex)
            {
                if (scan == generation) Fail(batch, ex.Message);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException)
            {
                if (scan == generation) Fail(batch, "אולי אין חיבור לאינטרנט.");
            }
            finally
            {
                sending = false;
                UpdateContribution();
            }
        }

        private void Fail(ContributionBatch batch, string message)
        {
            failedBatch = batch;
            failure = message;
        }
    }
}
