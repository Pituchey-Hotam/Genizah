namespace Genizah
{
    partial class CensorControls : Microsoft.Office.Tools.Ribbon.RibbonBase
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        public CensorControls()
            : base(Globals.Factory.GetRibbonFactory())
        {
            InitializeComponent();
        }

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
            Microsoft.Office.Tools.Ribbon.RibbonDialogLauncher ribbonDialogLauncherImpl1 = this.Factory.CreateRibbonDialogLauncher();
            this.tab1 = this.Factory.CreateRibbonTab();
            this.genizahGroup = this.Factory.CreateRibbonGroup();
            this.CensorNamesBtn = this.Factory.CreateRibbonButton();
            this.tab1.SuspendLayout();
            this.genizahGroup.SuspendLayout();
            this.SuspendLayout();
            // 
            // tab1
            // 
            this.tab1.ControlId.ControlIdType = Microsoft.Office.Tools.Ribbon.RibbonControlIdType.Office;
            this.tab1.ControlId.OfficeId = "TabReviewWord";
            this.tab1.Groups.Add(this.genizahGroup);
            this.tab1.Label = "TabReviewWord";
            this.tab1.Name = "tab1";
            // 
            // genizahGroup
            // 
            ribbonDialogLauncherImpl1.ScreenTip = "הגדרות צנזור";
            this.genizahGroup.DialogLauncher = ribbonDialogLauncherImpl1;
            this.genizahGroup.Items.Add(this.CensorNamesBtn);
            this.genizahGroup.Label = "גניזה";
            this.genizahGroup.Name = "genizahGroup";
            this.genizahGroup.DialogLauncherClick += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.GenizahGroup_DialogLauncherClick);
            // 
            // CensorNamesBtn
            // 
            this.CensorNamesBtn.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.CensorNamesBtn.Image = global::Genizah.Properties.Resources.imgb32;
            this.CensorNamesBtn.Label = "צנזר שמות";
            this.CensorNamesBtn.Name = "CensorNamesBtn";
            this.CensorNamesBtn.ShowImage = true;
            this.CensorNamesBtn.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.CensorNamesBtn_Click);
            // 
            // CensorControls
            // 
            this.Name = "CensorControls";
            this.RibbonType = "Microsoft.Word.Document";
            this.Tabs.Add(this.tab1);
            this.Load += new Microsoft.Office.Tools.Ribbon.RibbonUIEventHandler(this.CensorControls_Load);
            this.tab1.ResumeLayout(false);
            this.tab1.PerformLayout();
            this.genizahGroup.ResumeLayout(false);
            this.genizahGroup.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        internal Microsoft.Office.Tools.Ribbon.RibbonTab tab1;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup genizahGroup;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton CensorNamesBtn;
    }

    partial class ThisRibbonCollection
    {
        internal CensorControls CensorControls
        {
            get { return this.GetRibbon<CensorControls>(); }
        }
    }
}
