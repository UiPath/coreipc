﻿namespace UiPath.Ipc.TV
{
    partial class FormProject
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
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormProject));
            statusStrip1 = new StatusStrip();
            labelStatus = new ToolStripStatusLabel();
            progressBar = new ToolStripProgressBar();
            imageList1 = new ImageList(components);
            telemetryExplorer = new TelemetryExplorer();
            splitContainer1 = new SplitContainer();
            findResultSetView1 = new FindResultSetView();
            toolStrip1 = new ToolStrip();
            buttonScanOutgoing = new ToolStripButton();
            buttonExecute = new ToolStripButton();
            watchView1 = new WatchView();
            splitContainer2 = new SplitContainer();
            expressionEditor = new ExpressionEditor();
            statusStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer1).BeginInit();
            splitContainer1.Panel1.SuspendLayout();
            splitContainer1.Panel2.SuspendLayout();
            splitContainer1.SuspendLayout();
            toolStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer2).BeginInit();
            splitContainer2.Panel1.SuspendLayout();
            splitContainer2.Panel2.SuspendLayout();
            splitContainer2.SuspendLayout();
            SuspendLayout();
            // 
            // statusStrip1
            // 
            statusStrip1.Items.AddRange(new ToolStripItem[] { labelStatus, progressBar });
            statusStrip1.Location = new Point(0, 388);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(765, 22);
            statusStrip1.TabIndex = 0;
            statusStrip1.Text = "statusStrip1";
            // 
            // labelStatus
            // 
            labelStatus.Name = "labelStatus";
            labelStatus.Size = new Size(26, 17);
            labelStatus.Text = "Idle";
            // 
            // progressBar
            // 
            progressBar.Name = "progressBar";
            progressBar.Size = new Size(100, 16);
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.Visible = false;
            // 
            // imageList1
            // 
            imageList1.ColorDepth = ColorDepth.Depth32Bit;
            imageList1.ImageSize = new Size(16, 16);
            imageList1.TransparentColor = Color.Transparent;
            // 
            // telemetryExplorer
            // 
            telemetryExplorer.BackColor = Color.FromArgb(93, 107, 153);
            telemetryExplorer.BorderStyle = BorderStyle.Fixed3D;
            telemetryExplorer.Dock = DockStyle.Fill;
            telemetryExplorer.Location = new Point(0, 0);
            telemetryExplorer.Name = "telemetryExplorer";
            telemetryExplorer.Padding = new Padding(8);
            telemetryExplorer.Size = new Size(765, 221);
            telemetryExplorer.TabIndex = 1;
            // 
            // splitContainer1
            // 
            splitContainer1.Dock = DockStyle.Fill;
            splitContainer1.Location = new Point(0, 26);
            splitContainer1.Name = "splitContainer1";
            splitContainer1.Orientation = Orientation.Horizontal;
            // 
            // splitContainer1.Panel1
            // 
            splitContainer1.Panel1.Controls.Add(telemetryExplorer);
            // 
            // splitContainer1.Panel2
            // 
            splitContainer1.Panel2.Controls.Add(findResultSetView1);
            splitContainer1.Panel2.Padding = new Padding(8);
            splitContainer1.Size = new Size(765, 362);
            splitContainer1.SplitterDistance = 221;
            splitContainer1.TabIndex = 3;
            // 
            // findResultSetView1
            // 
            findResultSetView1.Dock = DockStyle.Fill;
            findResultSetView1.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            findResultSetView1.Location = new Point(8, 8);
            findResultSetView1.Name = "findResultSetView1";
            findResultSetView1.Size = new Size(749, 121);
            findResultSetView1.TabIndex = 0;
            // 
            // toolStrip1
            // 
            toolStrip1.BackColor = Color.FromArgb(204, 213, 240);
            toolStrip1.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            toolStrip1.Items.AddRange(new ToolStripItem[] { buttonScanOutgoing, buttonExecute });
            toolStrip1.Location = new Point(0, 0);
            toolStrip1.Name = "toolStrip1";
            toolStrip1.Size = new Size(765, 26);
            toolStrip1.TabIndex = 4;
            toolStrip1.Text = "toolStrip1";
            // 
            // buttonScanOutgoing
            // 
            buttonScanOutgoing.Image = (Image)resources.GetObject("buttonScanOutgoing.Image");
            buttonScanOutgoing.ImageTransparentColor = Color.Magenta;
            buttonScanOutgoing.Name = "buttonScanOutgoing";
            buttonScanOutgoing.Size = new Size(152, 23);
            buttonScanOutgoing.Text = "Scan Outgoing Calls";
            buttonScanOutgoing.Click += buttonScanOutgoing_Click;
            // 
            // buttonExecute
            // 
            buttonExecute.Image = (Image)resources.GetObject("buttonExecute.Image");
            buttonExecute.ImageTransparentColor = Color.Magenta;
            buttonExecute.Name = "buttonExecute";
            buttonExecute.Size = new Size(75, 23);
            buttonExecute.Text = "Execute";
            buttonExecute.Visible = false;
            buttonExecute.Click += buttonExecute_Click;
            // 
            // watchView1
            // 
            watchView1.BackColor = SystemColors.Window;
            watchView1.Dock = DockStyle.Fill;
            watchView1.Location = new Point(0, 0);
            watchView1.Name = "watchView1";
            watchView1.Padding = new Padding(8);
            watchView1.Size = new Size(765, 176);
            watchView1.TabIndex = 2;
            // 
            // splitContainer2
            // 
            splitContainer2.Dock = DockStyle.Fill;
            splitContainer2.Location = new Point(0, 26);
            splitContainer2.Name = "splitContainer2";
            splitContainer2.Orientation = Orientation.Horizontal;
            // 
            // splitContainer2.Panel1
            // 
            splitContainer2.Panel1.Controls.Add(expressionEditor);
            // 
            // splitContainer2.Panel2
            // 
            splitContainer2.Panel2.Controls.Add(watchView1);
            splitContainer2.Size = new Size(765, 362);
            splitContainer2.SplitterDistance = 182;
            splitContainer2.TabIndex = 5;
            // 
            // expressionEditor
            // 
            expressionEditor.Dock = DockStyle.Fill;
            expressionEditor.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            expressionEditor.Location = new Point(0, 0);
            expressionEditor.Name = "expressionEditor";
            expressionEditor.Size = new Size(765, 182);
            expressionEditor.TabIndex = 0;
            // 
            // FormProject
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(93, 107, 153);
            ClientSize = new Size(765, 410);
            Controls.Add(splitContainer2);
            Controls.Add(splitContainer1);
            Controls.Add(statusStrip1);
            Controls.Add(toolStrip1);
            Name = "FormProject";
            Text = "FormProject";
            Activated += FormProject_Activated;
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer1).EndInit();
            splitContainer1.ResumeLayout(false);
            toolStrip1.ResumeLayout(false);
            toolStrip1.PerformLayout();
            splitContainer2.Panel1.ResumeLayout(false);
            splitContainer2.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer2).EndInit();
            splitContainer2.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private StatusStrip statusStrip1;
        private ToolStripStatusLabel labelStatus;
        private ToolStripProgressBar progressBar;
        private ImageList imageList1;
        private TelemetryExplorer telemetryExplorer;
        private SplitContainer splitContainer1;
        private FindResultSetView findResultSetView1;
        private ToolStrip toolStrip1;
        private ToolStripButton buttonScanOutgoing;
        private WatchView watchView1;
        private SplitContainer splitContainer2;
        private ExpressionEditor expressionEditor;
        private ToolStripButton buttonExecute;
    }
}