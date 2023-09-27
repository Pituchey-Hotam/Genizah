using System;
using System.Windows.Forms;
using Genizah.Results;
using Microsoft.Office.Interop.Word;
using Application = Microsoft.Office.Interop.Word.Application;

namespace Genizah
{
    public partial class ResultControl : UserControl
    {
        private Bookmark Bookmark { get; set; } = null;
        private string OriginalText { get; set; }
        public int RangeStart
        { get { return this.Bookmark?.Start ?? -1; } }
        public int RangeEnd
        { get { return this.Bookmark?.End ?? -1; } }

        public event EventHandler RemoveResultControlHandler;
        public ResultControl(SearchResult result)
        {
            InitializeComponent();
            this.MouseEnter += this.OnMouseEnter;
            this.MouseLeave += this.OnMouseLeave;
            this.Click += ClickHandler_SelectResult;
            this.originalTextLabel.Click += ClickHandler_SelectResult;
            this.replacementTextLabel.Click += ClickHandler_SelectResult;
            this.Bookmark = result.Bookmark;
            this.OriginalText = result.OriginalText;
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
            Globals.ThisAddIn.Application.Selection.Start = this.RangeStart;
            Globals.ThisAddIn.Application.Selection.End = this.RangeEnd;
        }

        private void UndoHandler(object sender, EventArgs e)
        {
            var doc = Globals.ThisAddIn.Application.ActiveDocument;
            var range = doc.Range(this.RangeStart, this.RangeEnd);
            range.Text = this.OriginalText;
            range.HighlightColorIndex = 0;
            RemoveResultControlHandler?.Invoke(this, EventArgs.Empty);
        }
    }
}
