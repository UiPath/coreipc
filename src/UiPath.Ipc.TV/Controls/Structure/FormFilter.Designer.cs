namespace UiPath.Ipc.TV
{
    partial class FormFilter
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormFilter));
            expressionEditor = new ExpressionEditor();
            splitContainer1 = new SplitContainer();
            telemetryExplorer1 = new TelemetryExplorer();
            toolStrip = new ToolStrip();
            buttonExecute = new ToolStripButton();
            buttonCancel = new ToolStripButton();
            statusStrip1 = new StatusStrip();
            labelStatus = new ToolStripStatusLabel();
            progressBar = new ToolStripProgressBar();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
            toolStrip.SuspendLayout();
            statusStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // expressionEditor
            // 
            expressionEditor.Dock = DockStyle.Fill;
            expressionEditor.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            expressionEditor.Location = new Point(0, 0);
            expressionEditor.Name = "expressionEditor";
            expressionEditor.Size = new Size(639, 101);
            expressionEditor.TabIndex = 0;
            // 
            // splitContainer1
            // 
            splitContainer1.Dock = DockStyle.Fill;
            splitContainer1.Location = new Point(8, 8);
            splitContainer1.Name = "splitContainer1";
            splitContainer1.Orientation = Orientation.Horizontal;
            // 
            // splitContainer1.Panel1
            // 
            splitContainer1.Panel1.Controls.Add(expressionEditor);
            // 
            // splitContainer1.Panel2
            // 
            splitContainer1.Panel2.Controls.Add(telemetryExplorer1);
            splitContainer1.Panel2.Controls.Add(toolStrip);
            splitContainer1.Panel2.Controls.Add(statusStrip1);
            splitContainer1.Size = new Size(639, 324);
            splitContainer1.SplitterDistance = 101;
            splitContainer1.SplitterWidth = 8;
            splitContainer1.TabIndex = 2;
            // 
            // telemetryExplorer1
            // 
            telemetryExplorer1.BackColor = Color.FromArgb(93, 107, 153);
            telemetryExplorer1.BorderStyle = BorderStyle.Fixed3D;
            telemetryExplorer1.Dock = DockStyle.Fill;
            telemetryExplorer1.Location = new Point(0, 26);
            telemetryExplorer1.Name = "telemetryExplorer1";
            telemetryExplorer1.Padding = new Padding(0, 8, 0, 8);
            telemetryExplorer1.Size = new Size(639, 165);
            telemetryExplorer1.TabIndex = 2;
            // 
            // toolStrip
            // 
            toolStrip.BackColor = Color.FromArgb(204, 213, 240);
            toolStrip.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            toolStrip.Items.AddRange(new ToolStripItem[] { buttonExecute, buttonCancel });
            toolStrip.Location = new Point(0, 0);
            toolStrip.Name = "toolStrip";
            toolStrip.Size = new Size(639, 26);
            toolStrip.TabIndex = 1;
            toolStrip.Text = "toolStrip1";
            // 
            // buttonExecute
            // 
            buttonExecute.Image = (Image)resources.GetObject("buttonExecute.Image");
            buttonExecute.ImageTransparentColor = Color.Magenta;
            buttonExecute.Name = "buttonExecute";
            buttonExecute.Size = new Size(75, 23);
            buttonExecute.Text = "Execute";
            buttonExecute.Click += buttonExecute_Click;
            // 
            // buttonCancel
            // 
            buttonCancel.Image = (Image)resources.GetObject("buttonCancel.Image");
            buttonCancel.ImageTransparentColor = Color.Magenta;
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(69, 23);
            buttonCancel.Text = "Cancel";
            // 
            // statusStrip1
            // 
            statusStrip1.BackColor = Color.FromArgb(204, 213, 240);
            statusStrip1.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            statusStrip1.Items.AddRange(new ToolStripItem[] { labelStatus, progressBar });
            statusStrip1.Location = new Point(0, 191);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(639, 24);
            statusStrip1.TabIndex = 3;
            statusStrip1.Text = "statusStrip1";
            // 
            // labelStatus
            // 
            labelStatus.BackColor = SystemColors.Control;
            labelStatus.Name = "labelStatus";
            labelStatus.Size = new Size(135, 19);
            labelStatus.Text = "Filter not applied yet";
            // 
            // progressBar
            // 
            progressBar.BackColor = SystemColors.Control;
            progressBar.Name = "progressBar";
            progressBar.Size = new Size(100, 18);
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Visible = false;
            // 
            // FormFilter
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(93, 107, 153);
            ClientSize = new Size(655, 340);
            Controls.Add(splitContainer1);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            Name = "FormFilter";
            Padding = new Padding(8);
            Text = "Filter";
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel2.ResumeLayout(false);
            splitContainer1.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            toolStrip.ResumeLayout(false);
            toolStrip.PerformLayout();
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private ExpressionEditor expressionEditor;
        private SplitContainer splitContainer1;
        private TelemetryExplorer telemetryExplorer1;
        private ToolStrip toolStrip;
        private ToolStripButton buttonExecute;
        private StatusStrip statusStrip1;
        private ToolStripStatusLabel labelStatus;
        private ToolStripProgressBar progressBar;
        private ToolStripButton buttonCancel;
    }
}