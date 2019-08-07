using System.Threading.Tasks;

namespace UiPath.Ipc.TestChatServer
{
    public interface IChatCallback
    {
        Task<bool> ProcessSessionCreatedAsync(string sessionId, string nickname);
        Task<bool> ProcessSessionDestroyedAsync(string sessionId, string nickname);
        Task<bool> ProcessMessageSentAsync(string sessionId, string nickname, string message);
    }
}
