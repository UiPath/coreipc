using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
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
        private readonly ConcurrentDictionary<string, RequestCompletionSource> _requests = new();
        private readonly ISerializer _serializer;
        private long _requestCounter = -1;
        private readonly int _maxMessageSize;
        private readonly Lazy<Task> _receiveLoop;
        private readonly AsyncLock _sendLock = new();

        public Connection(Stream network, ISerializer serializer, ILogger logger, string name, int maxMessageSize = int.MaxValue)
        {
            Network = network;
            _serializer = serializer;
            Logger = logger;
            Name = $"{name} {GetHashCode()}";
            _maxMessageSize = maxMessageSize;
            _receiveLoop = new(ReceiveLoop);
        }
        public string NewRequestId() => Interlocked.Increment(ref _requestCounter).ToString();
        public Task Listen() => _receiveLoop.Value;
        internal event Func<Request, Stream, Task> RequestReceived;
        internal event Action<string> CancellationRequestReceived;
        public event EventHandler<EventArgs> Closed;
        internal async Task<Response> Send(Request request, Stream userStream, CancellationToken token)
        {
            var requestBytes = Serialize(request);
            var requestCompletion = new RequestCompletionSource();
            _requests[request.Id] = requestCompletion;
            try
            {
                await SendRequest(userStream, requestBytes, token);
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
                if (userStream == null)
                {
                    CancelServerCall(request.Id).LogException(Logger, this);
                }
                else
                {
                    Dispose();
                }
            }
            Task CancelServerCall(string requestId) => SendMessage(MessageType.CancellationRequest, Serialize(new CancellationRequest(requestId)), default);
        }
        internal Task Send(Response response, CancellationToken token) => SendResponse(Serialize(response), token);
        private async Task SendRequest(Stream userStream, byte[] requestBytes, CancellationToken cancellationToken)
        {
            if (userStream == null)
            {
                await SendMessage(MessageType.Request, requestBytes, cancellationToken);
                return;
            }
            await SendStream(userStream, cancellationToken).WaitAsync(cancellationToken);
            return;
            async Task SendStream(Stream userStream, CancellationToken cancellationToken)
            {
                using (await _sendLock.LockAsync())
                {
                    using (cancellationToken.Register(Dispose))
                    {
                        await Network.WriteMessage(new(MessageType.Upload, requestBytes), cancellationToken);
                        await Network.WriteBuffer(BitConverter.GetBytes(userStream.Length), cancellationToken);
                        const int DefaultCopyBufferSize = 81920;
                        await userStream.CopyToAsync(Network, DefaultCopyBufferSize, cancellationToken);
                    }
                }
            }
        }
        private Task SendResponse(byte[] responseBytes, CancellationToken cancellationToken) => SendMessage(MessageType.Response, responseBytes, cancellationToken);
        public Stream Network { get; }
        public ILogger Logger { get; }
        public string Name { get; }

        public override string ToString() => Name;

        private Task SendMessage(MessageType messageType, byte[] data, CancellationToken cancellationToken) => SendMessage(new(messageType, data)).WaitAsync(cancellationToken);

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
                    var data = message.Data;
                    if (message.MessageType == MessageType.Upload)
                    {
                        var lengthBytes = await Network.ReadBufferCheckLength(sizeof(long), default);
                        var userStreamLength = BitConverter.ToInt64(lengthBytes, 0);
                        using var userStream = Network.ReadSlice(userStreamLength);
                        await OnRequestReceived(data, userStream);
                        continue;
                    }
                    Action callback = message.MessageType switch
                    {
                        MessageType.Request when RequestReceived != null => () => OnRequestReceived(data),
                        MessageType.Response => () => OnResponseReceived(data),
                        MessageType.CancellationRequest when CancellationRequestReceived != null =>
                            () => CancellationRequestReceived(Deserialize<CancellationRequest>(data).RequestId),
                        _ => null
                    };
                    if (callback != null)
                    {
                        Task.Run(callback).LogException(Logger, this);
                    }
                    else
                    {
                        Logger?.LogInformation("Unknown message type " + message.MessageType);
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
        private Task OnRequestReceived(byte[] data, Stream userStream = null) => RequestReceived(Deserialize<Request>(data), userStream);
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
}