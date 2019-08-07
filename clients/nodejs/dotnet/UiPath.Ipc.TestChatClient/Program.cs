using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UiPath.Ipc.TestChatClient
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var model = FormConnect.GetModel(args);
            if (null == model)
            {
                return;
            }

            var (pipeName, nickname, bounds) = model;
            Application.Run(new FormMain(pipeName, nickname)
            {
                Bounds = bounds
            });
        }
    }
}
