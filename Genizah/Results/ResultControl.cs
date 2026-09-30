using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Genizah.Results;
using Genizah.Scanning;

namespace Genizah
{
    /// <summary>
    /// One row of the results pane. The pane reuses a few rows for however many results there
    /// are, so a row shows whichever result it was last bound to.
    /// </summary>
    public partial class ResultControl : UserControl
    {
        public SearchResult Result { get; private set; }

        /// <summary>Raised after the user censors or leaves the name.</summary>
        public event EventHandler StateChanged;

        public ResultControl()
        {
            InitializeComponent();
            this.MouseEnter += this.OnMouseEnter;
            this.MouseLeave += this.OnMouseLeave;
            foreach (Control c in new Control[] { this, headerPanel, nameLabel, statusLabel, contextLabel, actionsPanel, scoreLabel })
                c.Click += ClickHandler_SelectResult;
        }

        public void Bind(SearchResult result)
        {
            this.Result = result;
            nameLabel.Text = result.OriginalText;
            contextLabel.Text = result.ContextBefore + result.OriginalText + result.ContextAfter;
            scoreLabel.Text = VerdictText(result.Verdict) + " (" + result.Logit.ToString("0.00") + ")";
            toolTip1.SetToolTip(censorButton, "יוחלף ב: " + result.CensoredText);
            ShowState();
        }

        private static string VerdictText(Verdict verdict)
        {
            switch (verdict)
            {
                case Verdict.Censor: return "המודל: לצנזר";
                case Verdict.Ask: return "המודל: לא בטוח";
                default: return "המודל: להשאיר";
            }
        }

        private void ShowState()
        {
            switch (Result.State)
            {
                case ResultState.Pending: statusLabel.Text = "ממתין להכרעה"; break;
                case ResultState.Censored: statusLabel.Text = "צונזר: " + Result.CensoredText; break;
                default: statusLabel.Text = "הושאר"; break;
            }
            censorButton.Visible = Result.State != ResultState.Censored;
            leaveButton.Visible = Result.State == ResultState.Pending;
            undoButton.Visible = Result.State == ResultState.Censored;
            this.BackColor = Result.State == ResultState.Pending ? Color.LightYellow : SystemColors.Control;
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
            try
            {
                this.Result.Bookmark.Select();
            }
            catch (COMException)
            {
                // The name's text was deleted, or its document closed.
            }
        }

        private void CensorHandler(object sender, EventArgs e)
        {
            Act(Result.Censor, "צנזור שם");
        }

        private void LeaveHandler(object sender, EventArgs e)
        {
            Act(Result.Leave, "ביטול צנזור שם");
        }

        private void Act(Action action, string undoName)
        {
            var undo = Globals.ThisAddIn.Application.UndoRecord;
            undo.StartCustomRecord(undoName);
            try
            {
                action();
                Result.Highlight();
            }
            catch (COMException ex)
            {
                MessageBox.Show("לא ניתן לשנות את השם במסמך: " + ex.Message, "צנזור גניזה");
            }
            finally
            {
                undo.EndCustomRecord();
            }
            ShowState();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
