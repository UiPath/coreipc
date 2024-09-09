namespace UiPath.Ipc.TV
{
    partial class FormBuildAndDeploy
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
            richTextBox = new RichTextBox();
            panelCommands = new Panel();
            buttonOk = new Button();
            buttonCancel = new Button();
            panelCommands.SuspendLayout();
            SuspendLayout();
            // 
            // richTextBox
            // 
            richTextBox.BackColor = Color.Black;
            richTextBox.Dock = DockStyle.Fill;
            richTextBox.Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
            richTextBox.Location = new Point(0, 0);
            richTextBox.Name = "richTextBox";
            richTextBox.ReadOnly = true;
            richTextBox.Size = new Size(800, 395);
            richTextBox.TabIndex = 1;
            richTextBox.Text = "";
            // 
            // panelCommands
            // 
            panelCommands.BackColor = Color.FromArgb(204, 213, 240);
            panelCommands.Controls.Add(buttonOk);
            panelCommands.Controls.Add(buttonCancel);
            panelCommands.Dock = DockStyle.Bottom;
            panelCommands.Location = new Point(0, 395);
            panelCommands.Name = "panelCommands";
            panelCommands.Size = new Size(800, 55);
            panelCommands.TabIndex = 2;
            // 
            // buttonOk
            // 
            buttonOk.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonOk.DialogResult = DialogResult.OK;
            buttonOk.Enabled = false;
            buttonOk.Location = new Point(632, 10);
            buttonOk.Name = "buttonOk";
            buttonOk.Size = new Size(75, 34);
            buttonOk.TabIndex = 1;
            buttonOk.Text = "Ok";
            buttonOk.UseVisualStyleBackColor = true;
            // 
            // buttonCancel
            // 
            buttonCancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            buttonCancel.Enabled = false;
            buttonCancel.Location = new Point(713, 10);
            buttonCancel.Name = "buttonCancel";
            buttonCancel.Size = new Size(75, 34);
            buttonCancel.TabIndex = 1;
            buttonCancel.Text = "Cancel";
            buttonCancel.UseVisualStyleBackColor = true;
            buttonCancel.Click += buttonCancel_Click;
            // 
            // FormBuildAndDeploy
            // 
            AcceptButton = buttonOk;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            CancelButton = buttonCancel;
            ClientSize = new Size(800, 450);
            Controls.Add(richTextBox);
            Controls.Add(panelCommands);
            Name = "FormBuildAndDeploy";
            Text = "FormBuildAndDeploy";
            FormClosing += FormBuildAndDeploy_FormClosing;
            Load += FormBuildAndDeploy_Load;
            panelCommands.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private RichTextBox richTextBox;
        private Panel panelCommands;
        private Button buttonCancel;
        private Button buttonOk;
    }
}