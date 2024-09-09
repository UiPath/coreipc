namespace UiPath.Ipc.TV
{
    partial class RepoView
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
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(RepoView));
            imageList = new ImageList(components);
            listView = new DoubleBufferedListView();
            columnTimeStamp = new ColumnHeader();
            columnProcess = new ColumnHeader();
            columnVerb = new ColumnHeader();
            splitContainer1 = new SplitContainer();
            eeDbQueryPredicate = new ExpressionEditor();
            toolStrip1 = new ToolStrip();
            buttonRun = new ToolStripButton();
            progressBar = new ToolStripProgressBar();
            splitContainer2 = new SplitContainer();
            detailsPane1 = new DetailsPane2();
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
            // imageList
            // 
            imageList.ColorDepth = ColorDepth.Depth32Bit;
            imageList.ImageSize = new Size(16, 16);
            imageList.TransparentColor = Color.Transparent;
            // 
            // listView
            // 
            listView.Activation = ItemActivation.OneClick;
            listView.BorderStyle = BorderStyle.None;
            listView.Columns.AddRange(new ColumnHeader[] { columnTimeStamp, columnProcess, columnVerb });
            listView.Dock = DockStyle.Fill;
            listView.FullRowSelect = true;
            listView.Location = new Point(0, 0);
            listView.Name = "listView";
            listView.Size = new Size(729, 185);
            listView.SmallImageList = imageList;
            listView.TabIndex = 1;
            listView.UseCompatibleStateImageBehavior = false;
            listView.View = View.Details;
            listView.VirtualMode = true;
            listView.CacheVirtualItems += listView_CacheVirtualItems;
            listView.RetrieveVirtualItem += listView_RetrieveVirtualItem;
            listView.SelectedIndexChanged += listView_SelectedIndexChanged;
            // 
            // columnTimeStamp
            // 
            columnTimeStamp.Text = "Time Stamp";
            columnTimeStamp.Width = 140;
            // 
            // columnProcess
            // 
            columnProcess.Text = "Process";
            columnProcess.Width = 160;
            // 
            // columnVerb
            // 
            columnVerb.Text = "Verb";
            columnVerb.Width = 180;
            // 
            // splitContainer1
            // 
            splitContainer1.Dock = DockStyle.Fill;
            splitContainer1.Location = new Point(0, 0);
            splitContainer1.Name = "splitContainer1";
            splitContainer1.Orientation = Orientation.Horizontal;
            // 
            // splitContainer1.Panel1
            // 
            splitContainer1.Panel1.Controls.Add(eeDbQueryPredicate);
            splitContainer1.Panel1.Controls.Add(toolStrip1);
            // 
            // splitContainer1.Panel2
            // 
            splitContainer1.Panel2.Controls.Add(splitContainer2);
            splitContainer1.Size = new Size(729, 366);
            splitContainer1.SplitterDistance = 173;
            splitContainer1.SplitterWidth = 8;
            splitContainer1.TabIndex = 2;
            // 
            // eeDbQueryPredicate
            // 
            eeDbQueryPredicate.Dock = DockStyle.Fill;
            eeDbQueryPredicate.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            eeDbQueryPredicate.Location = new Point(0, 26);
            eeDbQueryPredicate.Name = "eeDbQueryPredicate";
            eeDbQueryPredicate.Size = new Size(729, 147);
            eeDbQueryPredicate.TabIndex = 0;
            // 
            // toolStrip1
            // 
            toolStrip1.BackColor = Color.FromArgb(204, 213, 240);
            toolStrip1.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            toolStrip1.Items.AddRange(new ToolStripItem[] { buttonRun, progressBar });
            toolStrip1.Location = new Point(0, 0);
            toolStrip1.Name = "toolStrip1";
            toolStrip1.Size = new Size(729, 26);
            toolStrip1.TabIndex = 2;
            toolStrip1.Text = "toolStrip1";
            // 
            // buttonRun
            // 
            buttonRun.Image = (Image)resources.GetObject("buttonRun.Image");
            buttonRun.ImageTransparentColor = Color.Magenta;
            buttonRun.Name = "buttonRun";
            buttonRun.Size = new Size(64, 23);
            buttonRun.Text = "Apply";
            buttonRun.Click += toolStripButton1_Click;
            // 
            // progressBar
            // 
            progressBar.Name = "progressBar";
            progressBar.Size = new Size(100, 23);
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.Visible = false;
            // 
            // splitContainer2
            // 
            splitContainer2.Dock = DockStyle.Fill;
            splitContainer2.Location = new Point(0, 0);
            splitContainer2.Name = "splitContainer2";
            // 
            // splitContainer2.Panel1
            // 
            splitContainer2.Panel1.Controls.Add(listView);
            // 
            // splitContainer2.Panel2
            // 
            splitContainer2.Panel2.Controls.Add(detailsPane1);
            splitContainer2.Panel2Collapsed = true;
            splitContainer2.Size = new Size(729, 185);
            splitContainer2.SplitterDistance = 407;
            splitContainer2.SplitterWidth = 8;
            splitContainer2.TabIndex = 3;
            // 
            // detailsPane1
            // 
            detailsPane1.BackColor = Color.FromArgb(93, 107, 153);
            detailsPane1.Dock = DockStyle.Fill;
            detailsPane1.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            detailsPane1.Location = new Point(0, 0);
            detailsPane1.Name = "detailsPane1";
            detailsPane1.Size = new Size(96, 100);
            detailsPane1.TabIndex = 0;
            // 
            // RepoView
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(splitContainer1);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            Name = "RepoView";
            Size = new Size(729, 366);
            splitContainer1.Panel1.ResumeLayout(false);
            splitContainer1.Panel1.PerformLayout();
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
        }

        #endregion
        private ImageList imageList;
        private DoubleBufferedListView listView;
        private ColumnHeader columnTimeStamp;
        private ColumnHeader columnProcess;
        private ColumnHeader columnVerb;
        private SplitContainer splitContainer1;
        private ExpressionEditor eeDbQueryPredicate;
        private ToolStrip toolStrip1;
        private ToolStripButton buttonRun;
        private ToolStripProgressBar progressBar;
        private SplitContainer splitContainer2;
        private DetailsPane2 detailsPane1;
    }
}
