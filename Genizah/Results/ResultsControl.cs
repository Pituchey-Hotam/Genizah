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

        public ResultsControl()
        {
            Controls.Add(new ElementHost { Dock = DockStyle.Fill, Child = view });
            view.ResultChanged += (sender, e) => ResultChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Shows a message in place of results: progress, or what went wrong.</summary>
        public void ShowMessage(string message) => view.ShowMessage(message);

        /// <inheritdoc cref="ResultsView.ShowSearchResults"/>
        public void ShowSearchResults(List<SearchResult> results, string notice) => view.ShowSearchResults(results, notice);
    }
}
