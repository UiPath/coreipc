using System;
using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace UiPath.CoreIpc
{
    public class Message
    {
        [JsonIgnore]
        internal EndpointSettings Endpoint { get; set; }
        [JsonIgnore]
        public IClient Client { get; set; }
        [JsonIgnore]
        public TimeSpan RequestTimeout { get; set; }
        public TCallbackInterface GetCallback<TCallbackInterface>() where TCallbackInterface : class => Client.GetCallback<TCallbackInterface>(Endpoint);
        public void ImpersonateClient(Action action) => Client.Impersonate(action);
    }
    public class Message<TPayload> : Message
    {
        public Message(TPayload payload) => Payload = payload;
        public TPayload Payload { get; }
    }
    public interface ICreateCallback
    {
        TCallbackInterface GetCallback<TCallbackInterface>(EndpointSettings endpoint) where TCallbackInterface : class;
    }
    public interface IClient : ICreateCallback
    {
        void Impersonate(Action action);
    }
    sealed class Client : IClient
    {
        private readonly Action<Action> _impersonationCallback;
        private readonly ICreateCallback _callbackFactory;
        private readonly ConcurrentDictionary<EndpointSettings, object> _callbacks = new ConcurrentDictionary<EndpointSettings, object>();
        public Client(Action<Action> impersonationCallback, ICreateCallback callbackFactory)
        {
            _impersonationCallback = impersonationCallback ?? throw new ArgumentNullException(nameof(impersonationCallback));
            _callbackFactory = callbackFactory ?? throw new ArgumentNullException(nameof(callbackFactory));
        }
        public void Impersonate(Action action) => _impersonationCallback(action);
        TCallbackInterface ICreateCallback.GetCallback<TCallbackInterface>(EndpointSettings endpoint) where TCallbackInterface : class =>
            (TCallbackInterface) _callbacks.GetOrAdd(endpoint, localEndpoint => _callbackFactory.GetCallback<TCallbackInterface>(localEndpoint));
    }
}