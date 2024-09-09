namespace UiPath.Ipc.TV
{
    partial class DetailsPane
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
            panel1 = new Panel();
            watchException = new WatchView();
            panel2 = new Panel();
            label1 = new Label();
            splitContainer = new SplitContainer();
            panel3 = new Panel();
            panel4 = new Panel();
            label2 = new Label();
            watchRecord = new WatchView();
            panel1.SuspendLayout();
            panel2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)splitContainer).BeginInit();
            splitContainer.Panel1.SuspendLayout();
            splitContainer.Panel2.SuspendLayout();
            splitContainer.SuspendLayout();
            panel3.SuspendLayout();
            panel4.SuspendLayout();
            SuspendLayout();
            // 
            // panel1
            // 
            panel1.Controls.Add(watchException);
            panel1.Controls.Add(panel2);
            panel1.Dock = DockStyle.Fill;
            panel1.Location = new Point(0, 0);
            panel1.Name = "panel1";
            panel1.Size = new Size(758, 247);
            panel1.TabIndex = 1;
            // 
            // watchException
            // 
            watchException.BackColor = SystemColors.Window;
            watchException.Dock = DockStyle.Fill;
            watchException.Location = new Point(0, 27);
            watchException.Name = "watchException";
            watchException.Padding = new Padding(8);
            watchException.Size = new Size(758, 220);
            watchException.TabIndex = 3;
            // 
            // panel2
            // 
            panel2.BackColor = Color.FromArgb(64, 86, 141);
            panel2.Controls.Add(label1);
            panel2.Dock = DockStyle.Top;
            panel2.Location = new Point(0, 0);
            panel2.Name = "panel2";
            panel2.Size = new Size(758, 27);
            panel2.TabIndex = 2;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            label1.ForeColor = Color.White;
            label1.Location = new Point(4, 6);
            label1.Name = "label1";
            label1.Size = new Size(143, 19);
            label1.TabIndex = 0;
            label1.Text = "Exception Information";
            // 
            // splitContainer
            // 
            splitContainer.Dock = DockStyle.Fill;
            splitContainer.Location = new Point(0, 0);
            splitContainer.Name = "splitContainer";
            splitContainer.Orientation = Orientation.Horizontal;
            // 
            // splitContainer.Panel1
            // 
            splitContainer.Panel1.Controls.Add(panel3);
            // 
            // splitContainer.Panel2
            // 
            splitContainer.Panel2.Controls.Add(panel1);
            splitContainer.Size = new Size(758, 505);
            splitContainer.SplitterDistance = 250;
            splitContainer.SplitterWidth = 8;
            splitContainer.TabIndex = 0;
            // 
            // panel3
            // 
            panel3.Controls.Add(watchRecord);
            panel3.Controls.Add(panel4);
            panel3.Dock = DockStyle.Fill;
            panel3.Location = new Point(0, 0);
            panel3.Name = "panel3";
            panel3.Size = new Size(758, 250);
            panel3.TabIndex = 0;
            // 
            // panel4
            // 
            panel4.BackColor = Color.FromArgb(64, 86, 141);
            panel4.Controls.Add(label2);
            panel4.Dock = DockStyle.Top;
            panel4.Location = new Point(0, 0);
            panel4.Name = "panel4";
            panel4.Size = new Size(758, 27);
            panel4.TabIndex = 3;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            label2.ForeColor = Color.White;
            label2.Location = new Point(4, 6);
            label2.Name = "label2";
            label2.Size = new Size(114, 19);
            label2.TabIndex = 0;
            label2.Text = "Telemetry Record";
            // 
            // watchRecord
            // 
            watchRecord.BackColor = SystemColors.Window;
            watchRecord.Dock = DockStyle.Fill;
            watchRecord.Location = new Point(0, 27);
            watchRecord.Name = "watchRecord";
            watchRecord.Padding = new Padding(8);
            watchRecord.Size = new Size(758, 223);
            watchRecord.TabIndex = 4;
            // 
            // DetailsPane
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(93, 107, 153);
            Controls.Add(splitContainer);
            Name = "DetailsPane";
            Size = new Size(758, 505);
            panel1.ResumeLayout(false);
            panel2.ResumeLayout(false);
            panel2.PerformLayout();
            splitContainer.Panel1.ResumeLayout(false);
            splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)splitContainer).EndInit();
            splitContainer.ResumeLayout(false);
            panel3.ResumeLayout(false);
            panel4.ResumeLayout(false);
            panel4.PerformLayout();
            ResumeLayout(false);
        }

        #endregion
        private Panel panel1;
        private SplitContainer splitContainer;
        private Panel panel2;
        private Label label1;
        private WatchView watchException;
        private Panel panel3;
        private WatchView watchRecord;
        private Panel panel4;
        private Label label2;
    }
}
