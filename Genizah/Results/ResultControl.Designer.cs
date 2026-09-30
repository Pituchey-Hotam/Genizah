namespace Genizah
{
    partial class ResultControl
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ResultControl));
            this.headerPanel = new System.Windows.Forms.Panel();
            this.statusLabel = new System.Windows.Forms.Label();
            this.nameLabel = new System.Windows.Forms.Label();
            this.contextLabel = new System.Windows.Forms.Label();
            this.actionsPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.censorButton = new System.Windows.Forms.Button();
            this.leaveButton = new System.Windows.Forms.Button();
            this.undoButton = new System.Windows.Forms.Button();
            this.scoreLabel = new System.Windows.Forms.Label();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.headerPanel.SuspendLayout();
            this.actionsPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // headerPanel
            //
            this.headerPanel.Controls.Add(this.statusLabel);
            this.headerPanel.Controls.Add(this.nameLabel);
            this.headerPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.headerPanel.Location = new System.Drawing.Point(4, 4);
            this.headerPanel.Name = "headerPanel";
            this.headerPanel.Size = new System.Drawing.Size(251, 24);
            this.headerPanel.TabIndex = 0;
            //
            // statusLabel
            //
            this.statusLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.statusLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            this.statusLabel.Location = new System.Drawing.Point(0, 0);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Padding = new System.Windows.Forms.Padding(0, 0, 8, 0);
            this.statusLabel.Size = new System.Drawing.Size(200, 24);
            this.statusLabel.TabIndex = 1;
            this.statusLabel.Text = "status";
            this.statusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // nameLabel
            //
            this.nameLabel.AutoSize = true;
            this.nameLabel.Dock = System.Windows.Forms.DockStyle.Right;
            this.nameLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Bold);
            this.nameLabel.Location = new System.Drawing.Point(200, 0);
            this.nameLabel.Name = "nameLabel";
            this.nameLabel.Size = new System.Drawing.Size(51, 20);
            this.nameLabel.TabIndex = 0;
            this.nameLabel.Text = "name";
            this.nameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // contextLabel
            //
            this.contextLabel.AutoEllipsis = true;
            this.contextLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.contextLabel.Location = new System.Drawing.Point(4, 28);
            this.contextLabel.Name = "contextLabel";
            this.contextLabel.Size = new System.Drawing.Size(251, 36);
            this.contextLabel.TabIndex = 1;
            this.contextLabel.Text = "context";
            //
            // actionsPanel
            //
            this.actionsPanel.Controls.Add(this.censorButton);
            this.actionsPanel.Controls.Add(this.leaveButton);
            this.actionsPanel.Controls.Add(this.undoButton);
            this.actionsPanel.Controls.Add(this.scoreLabel);
            this.actionsPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.actionsPanel.Location = new System.Drawing.Point(4, 64);
            this.actionsPanel.Name = "actionsPanel";
            this.actionsPanel.Size = new System.Drawing.Size(251, 34);
            this.actionsPanel.TabIndex = 2;
            this.actionsPanel.WrapContents = false;
            //
            // censorButton
            //
            this.censorButton.AutoSize = true;
            this.censorButton.Cursor = System.Windows.Forms.Cursors.Default;
            this.censorButton.Name = "censorButton";
            this.censorButton.Size = new System.Drawing.Size(60, 28);
            this.censorButton.TabIndex = 0;
            this.censorButton.Text = "צנזר";
            this.censorButton.UseVisualStyleBackColor = true;
            this.censorButton.Click += new System.EventHandler(this.CensorHandler);
            //
            // leaveButton
            //
            this.leaveButton.AutoSize = true;
            this.leaveButton.Cursor = System.Windows.Forms.Cursors.Default;
            this.leaveButton.Name = "leaveButton";
            this.leaveButton.Size = new System.Drawing.Size(60, 28);
            this.leaveButton.TabIndex = 1;
            this.leaveButton.Text = "השאר";
            this.toolTip1.SetToolTip(this.leaveButton, "אין זה שם קודש: להשאיר כפי שנכתב");
            this.leaveButton.UseVisualStyleBackColor = true;
            this.leaveButton.Click += new System.EventHandler(this.LeaveHandler);
            //
            // undoButton
            //
            this.undoButton.Cursor = System.Windows.Forms.Cursors.Default;
            this.undoButton.Image = ((System.Drawing.Image)(resources.GetObject("undoButton.Image")));
            this.undoButton.Name = "undoButton";
            this.undoButton.Size = new System.Drawing.Size(29, 28);
            this.undoButton.TabIndex = 2;
            this.toolTip1.SetToolTip(this.undoButton, "ביטול הצינזור");
            this.undoButton.UseVisualStyleBackColor = true;
            this.undoButton.Click += new System.EventHandler(this.LeaveHandler);
            //
            // scoreLabel
            //
            this.scoreLabel.Anchor = System.Windows.Forms.AnchorStyles.None;
            this.scoreLabel.AutoSize = true;
            this.scoreLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            this.scoreLabel.Name = "scoreLabel";
            this.scoreLabel.Size = new System.Drawing.Size(40, 16);
            this.scoreLabel.TabIndex = 3;
            this.scoreLabel.Text = "score";
            this.toolTip1.SetToolTip(this.scoreLabel, "הכרעת המודל, והציון שלו (logit)");
            //
            // ResultControl
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            // Docked Top in reverse order of adding: the header ends up on top.
            this.Controls.Add(this.actionsPanel);
            this.Controls.Add(this.contextLabel);
            this.Controls.Add(this.headerPanel);
            this.Cursor = System.Windows.Forms.Cursors.Hand;
            this.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.Name = "ResultControl";
            this.Padding = new System.Windows.Forms.Padding(4);
            this.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.Size = new System.Drawing.Size(259, 104);
            this.toolTip1.SetToolTip(this, "סימון");
            this.headerPanel.ResumeLayout(false);
            this.headerPanel.PerformLayout();
            this.actionsPanel.ResumeLayout(false);
            this.actionsPanel.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.Panel headerPanel;
        private System.Windows.Forms.Label nameLabel;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.Label contextLabel;
        private System.Windows.Forms.FlowLayoutPanel actionsPanel;
        private System.Windows.Forms.Button censorButton;
        private System.Windows.Forms.Button leaveButton;
        private System.Windows.Forms.Button undoButton;
        private System.Windows.Forms.Label scoreLabel;
        private System.Windows.Forms.ToolTip toolTip1;
    }
}
