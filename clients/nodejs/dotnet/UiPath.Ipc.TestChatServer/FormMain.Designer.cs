namespace UiPath.Ipc.TestChatServer
{
    partial class FormMain
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FormMain));
            this._listView = new System.Windows.Forms.ListView();
            this.columnSessionId = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnNickname = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnCreatedAt = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.SuspendLayout();
            // 
            // _listView
            // 
            this._listView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnSessionId,
            this.columnNickname,
            this.columnCreatedAt});
            this._listView.Dock = System.Windows.Forms.DockStyle.Fill;
            this._listView.HideSelection = false;
            this._listView.Location = new System.Drawing.Point(4, 4);
            this._listView.Name = "_listView";
            this._listView.Size = new System.Drawing.Size(447, 625);
            this._listView.TabIndex = 0;
            this._listView.UseCompatibleStateImageBehavior = false;
            this._listView.View = System.Windows.Forms.View.Details;
            // 
            // columnSessionId
            // 
            this.columnSessionId.Text = "Session Id";
            this.columnSessionId.Width = 131;
            // 
            // columnNickname
            // 
            this.columnNickname.Text = "Nickname";
            this.columnNickname.Width = 133;
            // 
            // columnCreatedAt
            // 
            this.columnCreatedAt.Text = "Created";
            this.columnCreatedAt.Width = 137;
            // 
            // FormMain
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(455, 633);
            this.Controls.Add(this._listView);
            this.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "FormMain";
            this.Padding = new System.Windows.Forms.Padding(4);
            this.Text = "UiPath Ipc TestChatServer";
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ListView _listView;
        private System.Windows.Forms.ColumnHeader columnSessionId;
        private System.Windows.Forms.ColumnHeader columnNickname;
        private System.Windows.Forms.ColumnHeader columnCreatedAt;
    }
}