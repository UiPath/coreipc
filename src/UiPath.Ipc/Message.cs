using System;
using Newtonsoft.Json;

namespace UiPath.Ipc
{
    public class Message
    {
        [JsonIgnore]
        public IClient Client { get; set; }
        [JsonIgnore]
        public string ClientUserName => Client.UserName;
        [JsonIgnore]
        public TimeSpan RequestTimeout { get; set; }
        public TCallbackInterface GetCallback<TCallbackInterface>() where TCallbackInterface : class => Client.GetCallback<TCallbackInterface>();
        public void ImpersonateClient(Action action) => Client.Impersonate(action);
    }

    public class Message<TPayload> : Message
    {
        public Message(TPayload payload) => Payload = payload;
        public TPayload Payload { get; }
    }

    public interface ICreateCallback
    {
        TCallbackInterface GetCallback<TCallbackInterface>() where TCallbackInterface : class;
    }

    public interface IClient : ICreateCallback
    {
        string UserName { get; }
        void Impersonate(Action action);
    }

    public sealed class Client : IClient
    {
        private readonly Action<Action> _impersonationCallback;
        private readonly ICreateCallback _callbackFactory;
        private object _callback;

        public Client(string userName, Action<Action> impersonationCallback, ICreateCallback callbackFactory)
        {
            _impersonationCallback = impersonationCallback ?? throw new ArgumentNullException(nameof(impersonationCallback));
            _callbackFactory = callbackFactory ?? throw new ArgumentNullException(nameof(callbackFactory));
            UserName = userName ?? throw new ArgumentNullException(nameof(userName));
        }

        public string UserName { get; }
        public void Impersonate(Action action) => _impersonationCallback(action);
        public TCallbackInterface GetCallback<TCallbackInterface>() where TCallbackInterface : class
        {
            if (_callback == null)
            {
                lock (_callbackFactory)
                {
                    if (_callback == null)
                    {
                        _callback = _callbackFactory.GetCallback<TCallbackInterface>();
                    }
                }
            }
            return (TCallbackInterface)_callback;
        }
    }
}