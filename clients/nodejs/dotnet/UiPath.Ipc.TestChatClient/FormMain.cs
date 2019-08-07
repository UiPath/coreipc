using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using UiPath.Ipc.NamedPipe;
using UiPath.Ipc.TestChatServer;

namespace UiPath.Ipc.TestChatClient
{
    public partial class FormMain : Form, IChatCallback
    {
        private readonly string _pipeName;
        private readonly string _nickname;

        private readonly IChatService _serviceClient;
        private readonly Task<string> _sessionId;

        public FormMain(string pipeName, string nickname)
        {
            InitializeComponent();

            _pipeName = pipeName;
            _nickname = nickname;

            var serviceCollection = new ServiceCollection();
            serviceCollection
                .AddLogging()
                .AddIpc();
            var serviceProvider = serviceCollection.BuildServiceProvider();

            var builder = new NamedPipeClientBuilder<IChatService>(pipeName).RequestTimeout(TimeSpan.FromMinutes(10));
            var proxy = new CallbackProxy<IChatService, IChatCallback>(builder, serviceProvider, this);
            _serviceClient = proxy.ServiceClient;

            _sessionId = _serviceClient.StartSessionAsync(new Message<string>(nickname), default);
        }

        private void Log(string text)
        {
            Action action = () =>
            {
                textBoxLog.Text += $"{text}\r\n";
                textBoxLog.SelectionStart = textBoxLog.Text.Length;
                textBoxLog.ScrollToCaret();
            };
            action.OnGui(this);
        }

        public Task<bool> ProcessMessageSentAsync(string sessionId, string nickname, string message)
        {
            Log($"{nickname}: \"{message}\"");
            return Task.FromResult(true);
        }

        public Task<bool> ProcessSessionCreatedAsync(string sessionId, string nickname)
        {
            Log($"SERVER: {nickname} connected as {new string(sessionId.Take(6).ToArray())}");
            return Task.FromResult(true);
        }

        public Task<bool> ProcessSessionDestroyedAsync(string sessionId, string nickname)
        {
            Log($"SERVER: {nickname} disconnected...");
            return Task.FromResult(true);
        }

        private async void ButtonSend_Click(object sender, EventArgs e)
        {
            var sessionId = await _sessionId;

            string text = textBoxMessage.Text;
            textBoxMessage.Text = "";
            await _serviceClient.BroadcastAsync(sessionId, text, default);
        }
    }

    static class ControlExtensions
    {
        public static void OnGui(this Action action, Control control)
        {
            if (control.InvokeRequired)
                control.BeginInvoke(action);
            else
                action();
        }
    }
}
