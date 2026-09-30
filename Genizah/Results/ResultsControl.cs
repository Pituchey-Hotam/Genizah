using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Genizah.Results;
using Genizah.Scanning;

namespace Genizah
{
    /// <summary>
    /// The results pane. It shows results in a few reused rows, rebinding them as the list
    /// scrolls: one control per result took about 50 ms and ten window handles each, and a
    /// process runs out of handles at 10,000.
    /// </summary>
    public partial class ResultsControl : UserControl
    {
        private List<SearchResult> results = new List<SearchResult>();
        /// <summary><see cref="results"/> in the order shown.</summary>
        private List<SearchResult> ordered = new List<SearchResult>();
        private string notice;
        private readonly List<ResultControl> rows = new List<ResultControl>();
        private bool layingOut;

        /// <summary>Raised after the user censors or leaves a name from the pane.</summary>
        public event EventHandler ResultChanged;

        public ResultsControl()
        {
            InitializeComponent();
            rowsPanel.ClientSizeChanged += (sender, e) => LayOutRows();
            scrollBar.ValueChanged += (sender, e) => LayOutRows();
            ScrollOnWheel(rowsPanel);
        }

        /// <summary>Shows a message in place of results: progress, or what went wrong.</summary>
        public void ShowMessage(string message)
        {
            results = new List<SearchResult>();
            ordered = results;
            notice = null;
            headerLabel.Text = message;
            LayOutRows();
        }

        /// <summary>
        /// Lists the results: the ones the model asked about first, then those it censored, then
        /// those it left, each group in document order. <paramref name="notice"/> is shown above them.
        /// </summary>
        public void ShowSearchResults(List<SearchResult> results, string notice)
        {
            this.results = results;
            this.ordered = results.OrderBy(r => Rank(r.Verdict)).ToList();
            this.notice = notice;
            scrollBar.Value = 0;
            LayOutRows();
            ShowSummary();
        }

        /// <summary>Binds and places just the rows the visible part of the list needs.</summary>
        private void LayOutRows()
        {
            if (layingOut) return;
            layingOut = true;
            rowsPanel.SuspendLayout();
            try
            {
                if (rows.Count == 0)
                    AddRow();
                int rowHeight = rows[0].Height + rows[0].Margin.Vertical;
                int viewport = rowsPanel.ClientSize.Height;
                int total = ordered.Count * rowHeight;
                int maxValue = Math.Max(0, total - viewport);
                // A scroll bar's value only reaches Maximum - LargeChange + 1.
                scrollBar.Maximum = Math.Max(0, total - 1);
                scrollBar.LargeChange = Math.Max(1, viewport);
                scrollBar.SmallChange = Math.Max(1, rowHeight / 2);
                scrollBar.Visible = total > viewport;
                if (scrollBar.Value > maxValue)
                    scrollBar.Value = maxValue;

                int first = scrollBar.Value / rowHeight, offset = scrollBar.Value % rowHeight;
                int needed = Math.Min(viewport / rowHeight + 2, ordered.Count - first);
                while (rows.Count < needed)
                    AddRow();
                for (int i = 0; i < rows.Count; i++)
                {
                    ResultControl row = rows[i];
                    if (i >= needed)
                    {
                        row.Visible = false;
                        continue;
                    }
                    if (row.Result != ordered[first + i])
                        row.Bind(ordered[first + i]);
                    row.SetBounds(row.Margin.Left, i * rowHeight - offset + row.Margin.Top,
                                  Math.Max(0, rowsPanel.ClientSize.Width - row.Margin.Horizontal), row.Height);
                    row.Visible = true;
                }
            }
            finally
            {
                rowsPanel.ResumeLayout();
                layingOut = false;
            }
        }

        private void AddRow()
        {
            var row = new ResultControl { Visible = false };
            row.StateChanged += (sender, e) =>
            {
                ShowSummary();
                ResultChanged?.Invoke(this, EventArgs.Empty);
            };
            ScrollOnWheel(row);
            rows.Add(row);
            rowsPanel.Controls.Add(row);
        }

        /// <summary>
        /// The wheel goes to the window under the mouse, which is usually one of a row's labels
        /// or buttons, so each of them scrolls the list.
        /// </summary>
        private void ScrollOnWheel(Control control)
        {
            control.MouseWheel += (sender, e) =>
            {
                if (!scrollBar.Visible) return;
                int rowHeight = rows.Count > 0 ? rows[0].Height + rows[0].Margin.Vertical : 1;
                int maxValue = Math.Max(0, scrollBar.Maximum - scrollBar.LargeChange + 1);
                scrollBar.Value = Math.Max(0, Math.Min(maxValue, scrollBar.Value - e.Delta * rowHeight / 120));
            };
            foreach (Control child in control.Controls)
                ScrollOnWheel(child);
        }

        private static int Rank(Verdict verdict)
        {
            switch (verdict)
            {
                case Verdict.Ask: return 0;
                case Verdict.Censor: return 1;
                default: return 2;
            }
        }

        private void ShowSummary()
        {
            string summary = results.Count == 0 ? "לא נמצאו שמות." :
                $"ממתינים להכרעה: {results.Count(r => r.State == ResultState.Pending)} · " +
                $"צונזרו: {results.Count(r => r.State == ResultState.Censored)} · " +
                $"הושארו: {results.Count(r => r.State == ResultState.Left)}";
            headerLabel.Text = notice == null ? summary : summary + "\n" + notice;
        }
    }
}
