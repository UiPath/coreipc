namespace UiPath.Ipc.TV
{
    partial class DetailsPane2
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
            watchRecord = new WatchView();
            panel4 = new Panel();
            label2 = new Label();
            panel4.SuspendLayout();
            SuspendLayout();
            // 
            // watchRecord
            // 
            watchRecord.BackColor = SystemColors.Window;
            watchRecord.Dock = DockStyle.Fill;
            watchRecord.Location = new Point(0, 27);
            watchRecord.Name = "watchRecord";
            watchRecord.Padding = new Padding(8);
            watchRecord.Size = new Size(531, 382);
            watchRecord.TabIndex = 6;
            // 
            // panel4
            // 
            panel4.BackColor = Color.FromArgb(64, 86, 141);
            panel4.Controls.Add(label2);
            panel4.Dock = DockStyle.Top;
            panel4.Location = new Point(0, 0);
            panel4.Name = "panel4";
            panel4.Size = new Size(531, 27);
            panel4.TabIndex = 5;
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
            // DetailsPane2
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(watchRecord);
            Controls.Add(panel4);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            Name = "DetailsPane2";
            Size = new Size(531, 409);
            panel4.ResumeLayout(false);
            panel4.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private WatchView watchRecord;
        private Panel panel4;
        private Label label2;
    }
}
