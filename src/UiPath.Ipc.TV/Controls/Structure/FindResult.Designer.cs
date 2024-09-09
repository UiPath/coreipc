namespace UiPath.Ipc.TV
{
    partial class FindResultView
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
            telemetryExplorer1 = new TelemetryExplorer();
            panel2 = new Panel();
            label1 = new Label();
            panel2.SuspendLayout();
            SuspendLayout();
            // 
            // telemetryExplorer1
            // 
            telemetryExplorer1.BackColor = Color.FromArgb(93, 107, 153);
            telemetryExplorer1.BorderStyle = BorderStyle.Fixed3D;
            telemetryExplorer1.Dock = DockStyle.Fill;
            telemetryExplorer1.Location = new Point(0, 27);
            telemetryExplorer1.Name = "telemetryExplorer1";
            telemetryExplorer1.Padding = new Padding(8);
            telemetryExplorer1.Size = new Size(620, 405);
            telemetryExplorer1.TabIndex = 0;
            // 
            // panel2
            // 
            panel2.BackColor = Color.FromArgb(64, 86, 141);
            panel2.Controls.Add(label1);
            panel2.Dock = DockStyle.Top;
            panel2.Location = new Point(0, 0);
            panel2.Name = "panel2";
            panel2.Size = new Size(620, 27);
            panel2.TabIndex = 3;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            label1.ForeColor = Color.White;
            label1.Location = new Point(4, 6);
            label1.Name = "label1";
            label1.Size = new Size(82, 19);
            label1.TabIndex = 0;
            label1.Text = "Find results:";
            // 
            // FindResult
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(telemetryExplorer1);
            Controls.Add(panel2);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            Name = "FindResult";
            Size = new Size(620, 432);
            panel2.ResumeLayout(false);
            panel2.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TelemetryExplorer telemetryExplorer1;
        private Panel panel2;
        private Label label1;
    }
}
