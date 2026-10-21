using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Genizah.Results;

namespace Genizah
{
    /// <summary>
    /// The results pane, as the task pane holds it: a Windows Forms control around the WPF
    /// <see cref="ResultsView"/>. WPF draws the whole list in one window and recycles its rows,
    /// so it scrolls smoothly however many results there are.
    /// </summary>
    public class ResultsControl : UserControl
    {
        private readonly ResultsView view = new ResultsView();

        /// <summary>Raised after the user censors or leaves a name from the pane.</summary>
        public event EventHandler ResultChanged;
        public event EventHandler ReviewRequested;
        public event EventHandler ScanRequested;

        public ResultsControl()
        {
            Controls.Add(new ElementHost { Dock = DockStyle.Fill, Child = view });
            view.ResultChanged += (sender, e) => ResultChanged?.Invoke(this, EventArgs.Empty);
            view.ReviewRequested += (sender, e) => ReviewRequested?.Invoke(this, EventArgs.Empty);
            view.ScanRequested += (sender, e) => ScanRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Shows a message in place of results: what went wrong, say.</summary>
        public void ShowMessage(string message) => view.ShowMessage(message);

        /// <inheritdoc cref="ResultsView.ShowProgress"/>
        public void ShowProgress(string stage, int done = 0, int total = 0) => view.ShowProgress(stage, done, total);

        /// <inheritdoc cref="ResultsView.ShowSearchResults"/>
        public void ShowSearchResults(List<SearchResult> results, string notice) => view.ShowSearchResults(results, notice);
        public void RefreshResults() => view.RefreshResults();
#if DEBUG
        internal void RequestScan() => view.RequestScan();
        internal string InspectReviewUi() => view.InspectReviewUi();
        internal bool ReviewResult(int index, bool censor) => view.ReviewResult(index, censor);
        internal void SelectResult(int index) => view.SelectResult(index);
#endif
    }
}
