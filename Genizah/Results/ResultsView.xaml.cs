using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        /// <summary>Raised after the user censors or leaves a name from the pane.</summary>
        public event EventHandler ResultChanged;

        public ResultsView()
        {
            InitializeComponent();
            list.ItemsSource = items;
        }

        /// <summary>Shows a message in place of results: what went wrong, say.</summary>
        public void ShowMessage(string message)
        {
            ClearResults();
            ShowProgressPanel(false);
            summary.Text = message;
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
        }

        private void ShowProgressPanel(bool show)
        {
            progressPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            summary.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            // Stops the moving bar's animation along with it.
            if (!show) progressBar.IsIndeterminate = false;
        }

        /// <summary>
        /// Lists the results in groups: the ones the model asked about, then those it censored, then
        /// those it left, each in document order. Only the first group starts expanded.
        /// <paramref name="notice"/> is shown above them.
        /// </summary>
        public void ShowSearchResults(List<SearchResult> results, string notice)
        {
            this.results = results;
            this.notice = notice;
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
                details.DataContext = item;
                details.Visibility = Visibility.Visible;
                try
                {
                    item.Result.Range.Select();
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
                if (items[i] is ResultItem next)
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
            bool done = false;
            var undo = Globals.ThisAddIn.Application.UndoRecord;
            undo.StartCustomRecord(undoName);
            try
            {
                action(item.Result);
                done = true;
            }
            catch (COMException ex)
            {
                System.Windows.Forms.MessageBox.Show("לא ניתן לשנות את השם במסמך: " + ex.Message, "צנזור גניזה");
            }
            finally
            {
                undo.EndCustomRecord();
            }
            item.Refresh();
            ShowSummary();
            ResultChanged?.Invoke(this, EventArgs.Empty);
            return done;
        }

        private void ShowSummary()
        {
            string text = results.Count == 0 ? "לא נמצאו שמות." :
                $"ממתינים להכרעה: {results.Count(r => r.State == ResultState.Pending)} · " +
                $"צונזרו: {results.Count(r => r.State == ResultState.Censored)} · " +
                $"הושארו: {results.Count(r => r.State == ResultState.Left)}";
            summary.Text = notice == null ? text : text + "\n" + notice;
        }
    }
}
