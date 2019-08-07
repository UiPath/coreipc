namespace UiPath.Ipc.TestChatServer
{
    public sealed partial class ChatService
    {
        internal readonly struct ConnectionInfo
        {
            public string Nickname { get; }
            public IClient Client { get; }
            public IChatCallback ChatCallback => Client.GetCallback<IChatCallback>();
            public string SessionId { get; }

            public ConnectionInfo(string nickname, IClient client, string sessionId)
            {
                Nickname = nickname;
                Client = client;
                SessionId = sessionId;
            }
        }
    }
}
