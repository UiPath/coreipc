namespace UiPath.Ipc.TV
{
    partial class FormRepo
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
            panelIndexing = new Panel();
            tableLayoutPanel1 = new TableLayoutPanel();
            panel2 = new Panel();
            progressBarIndexing = new ProgressBar();
            labelIndexing = new Label();
            repoView = new RepoView();
            panelIndexing.SuspendLayout();
            tableLayoutPanel1.SuspendLayout();
            panel2.SuspendLayout();
            SuspendLayout();
            // 
            // panelIndexing
            // 
            panelIndexing.Controls.Add(tableLayoutPanel1);
            panelIndexing.Dock = DockStyle.Fill;
            panelIndexing.Location = new Point(8, 8);
            panelIndexing.Name = "panelIndexing";
            panelIndexing.Size = new Size(784, 494);
            panelIndexing.TabIndex = 1;
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 3;
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 450F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            tableLayoutPanel1.Controls.Add(panel2, 1, 1);
            tableLayoutPanel1.Dock = DockStyle.Fill;
            tableLayoutPanel1.Location = new Point(0, 0);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 3;
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 200F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel1.Size = new Size(784, 494);
            tableLayoutPanel1.TabIndex = 0;
            // 
            // panel2
            // 
            panel2.Controls.Add(progressBarIndexing);
            panel2.Controls.Add(labelIndexing);
            panel2.Dock = DockStyle.Fill;
            panel2.Location = new Point(170, 150);
            panel2.Name = "panel2";
            panel2.Size = new Size(444, 194);
            panel2.TabIndex = 0;
            // 
            // progressBarIndexing
            // 
            progressBarIndexing.Location = new Point(172, 86);
            progressBarIndexing.Name = "progressBarIndexing";
            progressBarIndexing.Size = new Size(100, 23);
            progressBarIndexing.Style = ProgressBarStyle.Marquee;
            progressBarIndexing.TabIndex = 1;
            // 
            // labelIndexing
            // 
            labelIndexing.AutoSize = true;
            labelIndexing.ForeColor = Color.White;
            labelIndexing.Location = new Point(187, 64);
            labelIndexing.Name = "labelIndexing";
            labelIndexing.Size = new Size(70, 19);
            labelIndexing.TabIndex = 0;
            labelIndexing.Text = "Indexing...";
            // 
            // repoView
            // 
            repoView.Dock = DockStyle.Fill;
            repoView.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            repoView.Location = new Point(8, 8);
            repoView.Name = "repoView";
            repoView.Size = new Size(784, 494);
            repoView.TabIndex = 1;
            // 
            // FormRepo
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(93, 107, 153);
            ClientSize = new Size(800, 510);
            Controls.Add(panelIndexing);
            Controls.Add(repoView);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            Name = "FormRepo";
            Padding = new Padding(8);
            Text = "FormRepo";
            panelIndexing.ResumeLayout(false);
            tableLayoutPanel1.ResumeLayout(false);
            panel2.ResumeLayout(false);
            panel2.PerformLayout();
            ResumeLayout(false);
        }

        #endregion
        private Panel panelIndexing;
        private TableLayoutPanel tableLayoutPanel1;
        private Panel panel2;
        private Label labelIndexing;
        private ProgressBar progressBarIndexing;
        private RepoView repoView;
    }
}