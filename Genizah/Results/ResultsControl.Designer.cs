namespace Genizah
{
    partial class ResultsControl
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.rowsPanel = new System.Windows.Forms.Panel();
            this.scrollBar = new System.Windows.Forms.VScrollBar();
            this.headerLabel = new System.Windows.Forms.Label();
            this.SuspendLayout();
            //
            // rowsPanel
            //
            this.rowsPanel.BackColor = System.Drawing.SystemColors.Control;
            this.rowsPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rowsPanel.Location = new System.Drawing.Point(0, 72);
            this.rowsPanel.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.rowsPanel.Name = "rowsPanel";
            this.rowsPanel.Size = new System.Drawing.Size(250, 113);
            this.rowsPanel.TabIndex = 0;
            //
            // scrollBar
            //
            this.scrollBar.Dock = System.Windows.Forms.DockStyle.Left;
            this.scrollBar.Location = new System.Drawing.Point(0, 72);
            this.scrollBar.Name = "scrollBar";
            this.scrollBar.Size = new System.Drawing.Size(17, 113);
            this.scrollBar.TabIndex = 2;
            this.scrollBar.Visible = false;
            //
            // headerLabel
            //
            this.headerLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.headerLabel.Location = new System.Drawing.Point(0, 0);
            this.headerLabel.Name = "headerLabel";
            this.headerLabel.Padding = new System.Windows.Forms.Padding(4);
            this.headerLabel.Size = new System.Drawing.Size(267, 72);
            this.headerLabel.TabIndex = 1;
            this.headerLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // ResultsControl
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.rowsPanel);
            this.Controls.Add(this.scrollBar);
            this.Controls.Add(this.headerLabel);
            this.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.Name = "ResultsControl";
            this.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.Size = new System.Drawing.Size(267, 185);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel rowsPanel;
        private System.Windows.Forms.VScrollBar scrollBar;
        private System.Windows.Forms.Label headerLabel;
    }
}
