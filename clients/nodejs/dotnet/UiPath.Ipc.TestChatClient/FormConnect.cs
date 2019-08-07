using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UiPath.Ipc.TestChatClient
{
    public partial class FormConnect : Form
    {
        public sealed class Model
        {
            public string PipeName { get; set; }
            public string Nickname { get; set; }
            [Browsable(false)]
            public Rectangle Bounds { get; set; }

            public void Deconstruct(out string pipeName, out string nickname, out Rectangle bounds)
            {
                pipeName = PipeName;
                nickname = Nickname;
                bounds = Bounds;
            }
        }

        public static Model GetModel(string[] args = null)
        {
            var form = new FormConnect();
            var model = new Model
            {
                PipeName = args?.FirstOrDefault() ?? "test-char-server-pipe-name",
                Nickname = args?.Skip(1)?.FirstOrDefault() ?? "Kramer"
            };
            form.grid.SelectedObject = model;
            if (form.ShowDialog() != DialogResult.OK)
            {
                model = null;
            }
            else
            {
                model.Bounds = form.Bounds;
            }
            return model;
        }

        public FormConnect()
        {
            InitializeComponent();
        }
    }
}
