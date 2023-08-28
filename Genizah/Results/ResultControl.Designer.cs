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
            this.undoButton = new System.Windows.Forms.Button();
            this.originalTextLabel = new System.Windows.Forms.Label();
            this.replacementTextLabel = new System.Windows.Forms.Label();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.SuspendLayout();
            // 
            // undoButton
            // 
            this.undoButton.Image = ((System.Drawing.Image)(resources.GetObject("undoButton.Image")));
            this.undoButton.Location = new System.Drawing.Point(19, 3);
            this.undoButton.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.undoButton.Name = "undoButton";
            this.undoButton.Size = new System.Drawing.Size(22, 24);
            this.undoButton.TabIndex = 2;
            this.toolTip1.SetToolTip(this.undoButton, "ביטול הצינזור");
            this.undoButton.UseVisualStyleBackColor = true;
            this.undoButton.Click += new System.EventHandler(this.UndoHandler);
            // 
            // originalTextLabel
            // 
            this.originalTextLabel.AutoSize = true;
            this.originalTextLabel.Location = new System.Drawing.Point(138, 9);
            this.originalTextLabel.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.originalTextLabel.Name = "originalTextLabel";
            this.originalTextLabel.Size = new System.Drawing.Size(34, 13);
            this.originalTextLabel.TabIndex = 3;
            this.originalTextLabel.Text = "oText";
            this.originalTextLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // replacementTextLabel
            // 
            this.replacementTextLabel.AutoSize = true;
            this.replacementTextLabel.Location = new System.Drawing.Point(88, 9);
            this.replacementTextLabel.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.replacementTextLabel.Name = "replacementTextLabel";
            this.replacementTextLabel.Size = new System.Drawing.Size(31, 13);
            this.replacementTextLabel.TabIndex = 4;
            this.replacementTextLabel.Text = "rText";
            this.replacementTextLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // ResultControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.replacementTextLabel);
            this.Controls.Add(this.originalTextLabel);
            this.Controls.Add(this.undoButton);
            this.Cursor = System.Windows.Forms.Cursors.Hand;
            this.Margin = new System.Windows.Forms.Padding(2, 2, 2, 2);
            this.Name = "ResultControl";
            this.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.Size = new System.Drawing.Size(194, 32);
            this.toolTip1.SetToolTip(this, "סימון");
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.Button undoButton;
        private System.Windows.Forms.Label originalTextLabel;
        private System.Windows.Forms.Label replacementTextLabel;
        private System.Windows.Forms.ToolTip toolTip1;
    }
}
