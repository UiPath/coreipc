using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc
{
    using RequestCompletionSource = TaskCompletionSource<Response>;

    public sealed class Connection : IDisposable
    {
        private readonly ConcurrentDictionary<string, RequestCompletionSource> _requests = new ConcurrentDictionary<string, RequestCompletionSource>();
        private readonly ISerializer _serializer;
        private long _requestCounter = -1;
        private readonly int _maxMessageSize;
        private readonly Lazy<Task> _receiveLoop;
        private readonly AsyncLock _sendLock = new AsyncLock();

        public Connection(Stream network, ISerializer serializer, ILogger logger, string name, int maxMessageSize = int.MaxValue)
        {
            Network = network;
            _serializer = serializer;
            Logger = logger;
            Name = $"{name} {GetHashCode()}";
            _maxMessageSize = maxMessageSize;
            _receiveLoop = new Lazy<Task>(ReceiveLoop);
        }
        public string NewRequestId() => Interlocked.Increment(ref _requestCounter).ToString();
        public Task Listen() => _receiveLoop.Value;
        internal event EventHandler<RequestReceivedEventsArgs> RequestReceived;
        internal event EventHandler<CancellationRequestReceivedEventsArgs> CancellationRequestReceived;
        public event EventHandler<EventArgs> Closed;
        internal async Task<Response> Send(Request request, CancellationToken token)
        {
            var requestBytes = Serialize(request);
            var requestCompletion = new RequestCompletionSource();
            _requests[request.Id] = requestCompletion;
            try
            {
                await SendRequest(requestBytes, token);
                using (token.Register(CancelRequest))
                {
                    return await requestCompletion.Task;
                }
            }
            finally
            {
                _requests.TryRemove(request.Id, out _);
            }
            void CancelRequest()
            {
                requestCompletion.TrySetCanceled();
                CancelServerCall(request.Id).LogException(Logger, this);
            }
            Task CancelServerCall(string requestId) => SendMessage(MessageType.CancellationRequest, Serialize(new CancellationRequest(requestId)), CancellationToken.None);
        }
        internal Task Send(Response response, CancellationToken token) => SendResponse(Serialize(response), token);
        private Task SendRequest(byte[] requestBytes, CancellationToken cancellationToken) => SendMessage(MessageType.Request, requestBytes, cancellationToken);
        private Task SendResponse(byte[] responseBytes, CancellationToken cancellationToken) => SendMessage(MessageType.Response, responseBytes, cancellationToken);
        public Stream Network { get; }
        public ILogger Logger { get; }
        public string Name { get; }

        public override string ToString() => Name;

        private Task SendMessage(MessageType messageType, byte[] data, CancellationToken cancellationToken) => SendMessage(new WireMessage(messageType, data)).WaitAsync(cancellationToken);

        private async Task SendMessage(WireMessage wireMessage)
        {
            using (await _sendLock.LockAsync())
            {
                await Network.WriteMessage(wireMessage);
            }
        }

        public void Dispose()
        {
            Network.Dispose();
            try
            {
                CompleteRequests();
                Interlocked.Exchange(ref Closed, null)?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, this);
            }
        }

        private void CompleteRequests()
        {
            foreach (var completionSource in _requests.Values)
            {
                completionSource.TrySetException(new IOException("Connection closed."));
            }
        }

        private async Task ReceiveLoop()
        {
            WireMessage message;
            try
            {
                while (!(message = await Network.ReadMessage(_maxMessageSize)).Empty)
                {
                    Action callback = null;
                    var data = message.Data;
                    switch (message.MessageType)
                    {
                        case MessageType.Request:
                            if (RequestReceived != null)
                            {
                                callback = () => RequestReceived.Invoke(this, new RequestReceivedEventsArgs(Deserialize<Request>(data)));
                            }
                            break;
                        case MessageType.Response:
                            callback = () => OnResponseReceived(data);
                            break;
                        case MessageType.CancellationRequest:
                            if (CancellationRequestReceived != null)
                            {
                                callback = () => CancellationRequestReceived.Invoke(this, new CancellationRequestReceivedEventsArgs(Deserialize<CancellationRequest>(data)));
                            }
                            break;
                        default:
                            Logger?.LogInformation("Unknown message type " + message.MessageType);
                            break;
                    }
                    if (callback != null)
                    {
                        Task.Run(callback).LogException(Logger, this);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"{nameof(ReceiveLoop)} {Name}");
            }
            Logger?.LogInformation($"{nameof(ReceiveLoop)} {Name} finished.");
            Dispose();
        }
        private byte[] Serialize(object value) => _serializer.SerializeToBytes(value);
        private T Deserialize<T>(byte[] data) => _serializer.Deserialize<T>(data);
        private void OnResponseReceived(byte[] data)
        {
            var response = Deserialize<Response>(data);
            Logger?.LogInformation($"Received response for request {response.RequestId} {Name}.");
            if (_requests.TryGetValue(response.RequestId, out var completionSource))
            {
                completionSource.TrySetResult(response);
            }
        }
    }
    readonly struct RequestReceivedEventsArgs
    {
        public RequestReceivedEventsArgs(Request request) => Request = request;
        public Request Request { get; }
    }
    readonly struct CancellationRequestReceivedEventsArgs
    {
        public CancellationRequestReceivedEventsArgs(CancellationRequest cancellationRequest) => RequestId = cancellationRequest.RequestId;
        public string RequestId { get; }
    }
}