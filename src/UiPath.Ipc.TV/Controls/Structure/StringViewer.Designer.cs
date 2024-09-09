namespace UiPath.Ipc.TV
{
    partial class StringViewer
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
            panelControls = new Panel();
            buttonOk = new Button();
            textBox = new TextBox();
            panelControls.SuspendLayout();
            SuspendLayout();
            // 
            // panelControls
            // 
            panelControls.BackColor = Color.FromArgb(204, 213, 240);
            panelControls.Controls.Add(buttonOk);
            panelControls.Dock = DockStyle.Bottom;
            panelControls.Location = new Point(8, 314);
            panelControls.Name = "panelControls";
            panelControls.Size = new Size(784, 51);
            panelControls.TabIndex = 0;
            // 
            // buttonOk
            // 
            buttonOk.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonOk.DialogResult = DialogResult.OK;
            buttonOk.Location = new Point(695, 8);
            buttonOk.Name = "buttonOk";
            buttonOk.Size = new Size(75, 34);
            buttonOk.TabIndex = 0;
            buttonOk.Text = "Ok";
            buttonOk.UseVisualStyleBackColor = true;
            // 
            // textBox
            // 
            textBox.Dock = DockStyle.Fill;
            textBox.Location = new Point(8, 8);
            textBox.Multiline = true;
            textBox.Name = "textBox";
            textBox.ScrollBars = ScrollBars.Both;
            textBox.Size = new Size(784, 306);
            textBox.TabIndex = 1;
            // 
            // StringViewer
            // 
            AcceptButton = buttonOk;
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(93, 107, 153);
            CancelButton = buttonOk;
            ClientSize = new Size(800, 373);
            Controls.Add(textBox);
            Controls.Add(panelControls);
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            Name = "StringViewer";
            Padding = new Padding(8);
            StartPosition = FormStartPosition.CenterScreen;
            Text = "StringViewer";
            panelControls.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Panel panelControls;
        private Button buttonOk;
        private TextBox textBox;
    }
}