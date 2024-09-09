namespace UiPath.Ipc.TV
{
    partial class TelemetryExplorer
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(TelemetryExplorer));
            listView = new DoubleBufferedListView();
            columnTimeStamp = new ColumnHeader();
            columnProcess = new ColumnHeader();
            columnVerb = new ColumnHeader();
            imageList = new ImageList(components);
            panelNoModel = new Panel();
            tableLayoutPanel1 = new TableLayoutPanel();
            label1 = new Label();
            splitContainer = new SplitContainer();
            detailsPane = new DetailsPane();
            panelNoModel.SuspendLayout();
            tableLayoutPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.Panel2.SuspendLayout();
            splitContainer.SuspendLayout();
            SuspendLayout();
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
            listView.Size = new Size(673, 358);
            listView.SmallImageList = imageList;
            listView.TabIndex = 0;
            listView.UseCompatibleStateImageBehavior = false;
            listView.View = View.Details;
            listView.VirtualMode = true;
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
            // imageList
            // 
            imageList.ColorDepth = ColorDepth.Depth32Bit;
            imageList.ImageStream = (ImageListStreamer)resources.GetObject("imageList.ImageStream");
            imageList.TransparentColor = Color.Transparent;
            imageList.Images.SetKeyName(0, "Error");
            // 
            // panelNoModel
            // 
            panelNoModel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            panelNoModel.Controls.Add(tableLayoutPanel1);
            panelNoModel.Location = new Point(8, 33);
            panelNoModel.Name = "panelNoModel";
            panelNoModel.Size = new Size(670, 330);
            panelNoModel.TabIndex = 1;
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 3;
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel1.Controls.Add(label1, 1, 1);
            tableLayoutPanel1.Dock = DockStyle.Fill;
            tableLayoutPanel1.Location = new Point(0, 0);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 3;
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel1.Size = new Size(670, 330);
            tableLayoutPanel1.TabIndex = 0;
            // 
            // label1
            // 
            label1.Dock = DockStyle.Fill;
            label1.Location = new Point(228, 155);
            label1.Name = "label1";
            label1.Size = new Size(214, 20);
            label1.TabIndex = 0;
            label1.Text = "No Data";
            label1.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // splitContainer
            // 
            splitContainer.Dock = DockStyle.Fill;
            splitContainer.Location = new Point(8, 8);
            splitContainer.Name = "splitContainer";
            // 
            // splitContainer.Panel1
            // 
            splitContainer.Panel1.Controls.Add(listView);
            // 
            // splitContainer.Panel2
            // 
            splitContainer.Panel2.Controls.Add(detailsPane);
            splitContainer.Panel2Collapsed = true;
            splitContainer.Size = new Size(673, 358);
            splitContainer.SplitterDistance = 330;
            splitContainer.SplitterWidth = 8;
            splitContainer.TabIndex = 2;
            // 
            // detailsPane
            // 
            detailsPane.BackColor = Color.FromArgb(93, 107, 153);
            detailsPane.Dock = DockStyle.Fill;
            detailsPane.Location = new Point(0, 0);
            detailsPane.Name = "detailsPane";
            detailsPane.Size = new Size(335, 358);
            detailsPane.TabIndex = 0;
            // 
            // TelemetryExplorer
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(93, 107, 153);
            BorderStyle = BorderStyle.Fixed3D;
            Controls.Add(splitContainer);
            Controls.Add(panelNoModel);
            Name = "TelemetryExplorer";
            Padding = new Padding(8);
            Size = new Size(689, 374);
            panelNoModel.ResumeLayout(false);
            tableLayoutPanel1.ResumeLayout(false);
            splitContainer.Panel1.ResumeLayout(false);
            splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private DoubleBufferedListView listView;
        private ColumnHeader columnTimeStamp;
        private ColumnHeader columnProcess;
        private ColumnHeader columnVerb;
        private ImageList imageList;
        private Panel panelNoModel;
        private TableLayoutPanel tableLayoutPanel1;
        private Label label1;
        private SplitContainer splitContainer;
        private DetailsPane detailsPane;
    }
}
