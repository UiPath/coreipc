using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UiPath.Ipc;

namespace UiPath.Ipc.TestChatServer
{
    public interface IChatService
    {
        Task<string> StartSessionAsync(Message<string> nickname, CancellationToken cancellationToken);
        Task<int> BroadcastAsync(string sessionId, string text, CancellationToken cancellationToken);
        Task<bool> EndSessionAsync(string sessionId, CancellationToken cancellationToken);
    }
}
