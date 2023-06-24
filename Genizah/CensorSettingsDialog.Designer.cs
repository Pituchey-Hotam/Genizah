namespace Genizah
{
    [System.ComponentModel.DesignerCategory("")]
    partial class CensorSettingsDialog
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.tableLayoutPanel = new System.Windows.Forms.TableLayoutPanel();
            this.labels = new System.Windows.Forms.Label[NameInfo.names.Length];
            this.comboBoxes = new System.Windows.Forms.ComboBox[NameInfo.names.Length];

            for (int i = 0; i < NameInfo.names.Length; i++)
            {
                this.labels[i] = new System.Windows.Forms.Label();
                this.comboBoxes[i] = new System.Windows.Forms.ComboBox();
                
                this.comboBoxes[i].Dock = System.Windows.Forms.DockStyle.Fill;
                this.comboBoxes[i].FormattingEnabled = true;
                this.comboBoxes[i].Name = "comboBox" + i;
                this.comboBoxes[i].TabIndex = i;

                this.labels[i].AutoSize = true;
                this.labels[i].Dock = System.Windows.Forms.DockStyle.Fill;
                this.labels[i].Name = "label" + i;
                this.labels[i].Text = NameInfo.names[i].DisplayName;
                this.labels[i].TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            }

            this.okButton = new System.Windows.Forms.Button();
            this.tableLayoutPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // tableLayoutPanel
            // 
            this.tableLayoutPanel.ColumnCount = 2;
            this.tableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 35F));
            this.tableLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 65F));
            for (int i = 0; i < NameInfo.names.Length; i++)
            {
                this.tableLayoutPanel.Controls.Add(this.labels[i], 0, i);
                this.tableLayoutPanel.Controls.Add(this.comboBoxes[i], 1, i);
            }
            this.tableLayoutPanel.Location = new System.Drawing.Point(12, 12);
            this.tableLayoutPanel.Name = "tableLayoutPanel";
            this.tableLayoutPanel.RowCount = 8;
            this.tableLayoutPanel.Size = new System.Drawing.Size(227, 250);
            this.tableLayoutPanel.TabIndex = 0;
            // 
            // saveButton
            // 
            this.okButton.Location = new System.Drawing.Point(12, 260);
            this.okButton.Name = "okButton";
            this.okButton.Size = new System.Drawing.Size(75, 28);
            this.okButton.TabIndex = NameInfo.names.Length;
            this.okButton.Text = "סגור";
            this.okButton.Click += OkButton_Click;
            this.okButton.UseVisualStyleBackColor = true;
            // 
            // CensorSettingsDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.ClientSize = new System.Drawing.Size(267, 349);
            this.Controls.Add(this.okButton);
            this.Controls.Add(this.tableLayoutPanel);
            this.AcceptButton = this.okButton;
            this.Name = "CensorSettingsDialog";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "CensorSettingsDialog";
            this.FormClosed += CensorSettingsDialog_FormClosed;
            this.tableLayoutPanel.ResumeLayout(false);
            this.tableLayoutPanel.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel;
        private System.Windows.Forms.ComboBox[] comboBoxes;
        private System.Windows.Forms.Label[] labels;
        private System.Windows.Forms.Button okButton;
    }
}