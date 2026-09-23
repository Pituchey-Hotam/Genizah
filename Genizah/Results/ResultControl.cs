using System;
using System.Windows.Forms;
using Genizah.Results;
using Microsoft.Office.Interop.Word;
using Microsoft.Office.Tools.Word;
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

        private void UndoHandler(object sender, EventArgs e)
        {
            this.Result.UndoReplacement(); 
            RemoveResultControlHandler?.Invoke(this, EventArgs.Empty);
        }
    }
}
