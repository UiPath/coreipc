using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using UiPath.Ipc.NamedPipe;
using UiPath.Ipc.Tests;

namespace UiPath.Ipc.TestChatServer
{
    public partial class FormMain : Form
    {
        private readonly string _pipeName;
        private readonly Dictionary<string, ListViewItem> _items = new Dictionary<string, ListViewItem>();

        public FormMain(string pipeName)
        {
            _pipeName = pipeName;

            InitializeComponent();

            var serviceProvider = ConfigureServices();
            var host = new ServiceHostBuilder(serviceProvider)
                .AddEndpoint(new NamedPipeEndpointSettings<IChatService, IChatCallback>(pipeName)
                {
                    RequestTimeout = TimeSpan.FromHours(1),
                    AccessControl = security => { },
                })
                .Build();

            using (GuiLikeSyncContext.Install())
            {
                var _ = host.RunAsync(TaskScheduler.FromCurrentSynchronizationContext());
            }

            Trace.WriteLine($"Server is running with pipe \"{pipeName}\". Press CTRL+C to terminate...");
        }

        private IServiceProvider ConfigureServices() => new ServiceCollection()
            .AddLogging()
            .AddIpc()
            .AddSingleton<FormMain>(this)
            .AddSingleton<IChatService, ChatService>()
            .BuildServiceProvider();

        internal void PresentSessionCreated(ChatService.ConnectionInfo connectionInfo)
        {
            Action action = () =>
            {
                var item = _items[connectionInfo.SessionId] = new ListViewItem(new[] {
                    connectionInfo.SessionId,
                    connectionInfo.Nickname,
                    DateTime.Now.ToShortTimeString()
                })
                {
                    Tag = connectionInfo
                };

                _listView.Items.Add(item);
            };
            this.BeginInvokeIfNeeded(action);
        }
        internal void PresentMessageSent(ChatService.ConnectionInfo connectionInfo)
        {
            Action action = () =>
            {
            };
            this.BeginInvokeIfNeeded(action);
        }
        internal void PresentSessionDestroyed(ChatService.ConnectionInfo connectionInfo)
        {
            Action action = () =>
            {
                if (_items.TryGetValue(connectionInfo.SessionId, out var item))
                {
                    _items.Remove(connectionInfo.SessionId);
                    _listView.Items.Remove(item);
                }
            };
            this.BeginInvokeIfNeeded(action);
        }
    }

    static class ControlExtensions
    {
        public static void BeginInvokeIfNeeded(this Control control, Action action)
        {
            if (control.InvokeRequired)
            {
                control.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
    }
}
