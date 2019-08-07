using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using UiPath.Ipc;
using UiPath.Ipc.NamedPipe;
using UiPath.Ipc.Tests;

namespace UiPath.Ipc.TestChatServer
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            string pipeName = args[0];

            Application.SetCompatibleTextRenderingDefault(false);
            Application.EnableVisualStyles();
            Application.Run(new FormMain(pipeName));
        }

    }
}
