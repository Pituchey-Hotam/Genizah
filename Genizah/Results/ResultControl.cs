using System;
using System.Windows.Forms;
using Genizah.Results;
using Microsoft.Office.Interop.Word;
using Application = Microsoft.Office.Interop.Word.Application;

namespace Genizah
{
    public partial class ResultControl : UserControl
    {
        private SearchResult Result;

        public event EventHandler RemoveResultControlHandler;
        public ResultControl(SearchResult result)
        {
            InitializeComponent();
            this.MouseEnter += this.OnMouseEnter;
            this.MouseLeave += this.OnMouseLeave;
            this.Click += ClickHandler_SelectResult;
            this.originalTextLabel.Click += ClickHandler_SelectResult;
            this.replacementTextLabel.Click += ClickHandler_SelectResult;
            this.Result = result;
            originalTextLabel.Text = result.OriginalText;
            replacementTextLabel.Text = result.ReplacementText;
        }
        private void OnMouseEnter(object sender, EventArgs e)
        {
            this.BorderStyle = BorderStyle.FixedSingle;
        }
        private void OnMouseLeave(object sender, EventArgs e)
        {
            if (!this.ClientRectangle.Contains(this.PointToClient(Control.MousePosition)))
                this.BorderStyle = BorderStyle.None;
        }
        private void ClickHandler_SelectResult(object sender, EventArgs e)
        {
            this.Result.Bookmark.Select();
        }

        private const WdColorIndex INVALID_COLOR = (WdColorIndex) 9999999;

        private void UndoHandler(object sender, EventArgs e)
        {
            var doc = Globals.ThisAddIn.Application.ActiveDocument;
            var range = this.Result.Bookmark.Range;
            range.Text = this.Result.OriginalText;
            // If the original text was partially highlighted (or had multiple highlight colors), INVALID_COLOR will be returned.
            // For now, we just don't keep the highlight information in this case.
            if (this.Result.OriginalHighlight != INVALID_COLOR)
            {
                range.HighlightColorIndex = this.Result.OriginalHighlight;
            }
            else
            {
                range.HighlightColorIndex = WdColorIndex.wdNoHighlight;
            }
            RemoveResultControlHandler?.Invoke(this, EventArgs.Empty);
        }
    }
}
