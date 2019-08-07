using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.Ipc.TestChatServer
{
    public sealed partial class ChatService : IChatService
    {
        private readonly Dictionary<string, ConnectionInfo> _connections = new Dictionary<string, ConnectionInfo>();
        private readonly object _lock = new object();

        private readonly FormMain _formMain;

        public ChatService(FormMain formMain)
        {
            _formMain = formMain;
        }

        private static string GenerateSessionId()
        {
            using (var rng = new RNGCryptoServiceProvider())
            {
                byte[] tokenData = new byte[128];
                rng.GetBytes(tokenData);

                string sessionId = Convert.ToBase64String(tokenData);
                return sessionId;
            }
        }

        public async Task<string> StartSessionAsync(Message<string> nickname, CancellationToken cancellationToken)
        {
            var connectionInfo = new ConnectionInfo(nickname.Payload, nickname.Client, ChatService.GenerateSessionId());

            ConnectionInfo[] all;
            lock (_lock)
            {
                _connections[connectionInfo.SessionId] = connectionInfo;
                all = _connections.Values.ToArray();
            }

            _formMain.PresentSessionCreated(connectionInfo);

            await CallbackAsync(all, async x =>
            {
                await x.ChatCallback.ProcessSessionCreatedAsync(connectionInfo.SessionId, connectionInfo.Nickname);
            });

            return connectionInfo.SessionId;
        }
        public async Task<int> BroadcastAsync(string sessionId, string text, CancellationToken cancellationToken)
        {
            bool foundSelf;
            ConnectionInfo self;
            ConnectionInfo[] all = null;
            lock (_lock)
            {
                foundSelf = _connections.TryGetValue(sessionId, out self);
                if (foundSelf)
                {
                    all = _connections.Select(pair => pair.Value).ToArray();
                }
            }

            if (foundSelf)
            {
                _formMain.PresentMessageSent(self);
                await CallbackAsync(all, async x =>
                {
                    await x.ChatCallback.ProcessMessageSentAsync(sessionId, self.Nickname, text);
                });
            }

            return all.Length;
        }
        public async Task<bool> EndSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            bool didRemove;
            ConnectionInfo self;
            ConnectionInfo[] others = null;        

            lock (_lock)
            {
                _connections.TryGetValue(sessionId, out self);
                didRemove = _connections.Remove(sessionId);
                if (didRemove)
                {
                    others = _connections.Where(pair => pair.Key != sessionId).Select(pair => pair.Value).ToArray();
                }
            }

            if (didRemove)
            {
                _formMain.PresentSessionDestroyed(self);
                await CallbackAsync(others, async x =>
                {
                    await x.ChatCallback.ProcessSessionDestroyedAsync(sessionId, self.Nickname);
                });
            }
            return didRemove;
        }

        private async Task CallbackAsync(IEnumerable<ConnectionInfo> connectionInfos, Func<ConnectionInfo, Task> asyncAction)
        {
            var deadConnectionInfos = new List<ConnectionInfo>();
            foreach (var connectionInfo in connectionInfos)
            {
                try
                {
                    await asyncAction(connectionInfo);
                }
                catch
                {
                    deadConnectionInfos.Add(connectionInfo);
                }
            }

            if (deadConnectionInfos.Any())
            {
                lock (_lock)
                {
                    foreach (var connectionInfo in deadConnectionInfos)
                    {
                        _connections.Remove(connectionInfo.SessionId);
                        _formMain.PresentSessionDestroyed(connectionInfo);
                    }
                }
            }
        }
    }
}
