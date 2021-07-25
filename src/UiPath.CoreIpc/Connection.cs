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
        internal async Task<Response> Call(Request request, Stream userStream, CancellationToken token)
        {
            var requestBytes = Serialize(request);
            var requestCompletion = new RequestCompletionSource();
            _requests[request.Id] = requestCompletion;
            try
            {
                await SendRequest(requestBytes, userStream, token);
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
        internal Task Send(Response response, CancellationToken token) => SendResponse(Serialize(response), response.UserStream, token);
        private async Task SendRequest(byte[] requestBytes, Stream userStream, CancellationToken cancellationToken)
        {
            if (userStream == null)
            {
                await SendMessage(MessageType.Request, requestBytes, cancellationToken);
                return;
            }
            await SendStream(MessageType.Upload, requestBytes, userStream, cancellationToken);
        }
        private Task SendStream(MessageType messageType, byte[] data, Stream userStream, CancellationToken cancellationToken) =>
            SendStream(new(messageType, data), userStream, cancellationToken);
        private async Task SendStream(WireMessage message, Stream userStream, CancellationToken cancellationToken)
        {
            using (await _sendLock.LockAsync(cancellationToken))
            {
                using (cancellationToken.Register(Dispose))
                {
                    await Network.WriteMessage(message, cancellationToken);
                    await Network.WriteBuffer(BitConverter.GetBytes(userStream.Length), cancellationToken);
                    const int DefaultCopyBufferSize = 81920;
                    await userStream.CopyToAsync(Network, DefaultCopyBufferSize, cancellationToken);
                }
            }
        }
        private async Task SendResponse(byte[] responseBytes, Stream userStream, CancellationToken cancellationToken)
        {
            if (userStream == null)
            {
                await SendMessage(MessageType.Response, responseBytes, cancellationToken);
                return;
            }
            using (userStream)
            {
                await SendStream(MessageType.Download, responseBytes, userStream, cancellationToken);
            }
        }
        public Stream Network { get; }
        public ILogger Logger { get; }
        public string Name { get; }

        public override string ToString() => Name;

        private Task SendMessage(MessageType messageType, byte[] data, CancellationToken cancellationToken) => 
            SendMessage(new(messageType, data), cancellationToken).WaitAsync(cancellationToken);

        private async Task SendMessage(WireMessage wireMessage, CancellationToken cancellationToken)
        {
            using (await _sendLock.LockAsync(cancellationToken))
            {
                await Network.WriteMessage(wireMessage, CancellationToken.None);
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
                    await HandleMessage(message);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"{nameof(ReceiveLoop)} {Name}");
            }
            Logger?.LogInformation($"{nameof(ReceiveLoop)} {Name} finished.");
            Dispose();
            return;
            async Task HandleMessage(WireMessage message)
            {
                var data = message.Data;
                Action callback = null;
                switch (message.MessageType)
                {
                    case MessageType.Response:
                        callback = () => OnResponseReceived(data);
                        break;
                    case MessageType.Request when RequestReceived != null:
                        callback = () => OnRequestReceived(data);
                        break;
                    case MessageType.CancellationRequest when CancellationRequestReceived != null:
                        callback = () => CancellationRequestReceived(Deserialize<CancellationRequest>(data).RequestId);
                        break;
                    case MessageType.Upload:
                        await OnUpload(data);
                        break;
                    case MessageType.Download:
                        await OnDownload(data);
                        break;
                    default:
                        Logger?.LogInformation("Unknown message type " + message.MessageType);
                        break;
                };
                if (callback != null)
                {
                    Task.Run(callback).LogException(Logger, this);
                }
            }
        }

        private async Task OnDownload(byte[] data)
        {
            var downloadStream = await GetUserStream();
            var streamDisposed = new TaskCompletionSource<bool>();
            downloadStream.Disposed += delegate { streamDisposed.TrySetResult(true); };
            OnResponseReceived(data, downloadStream);
            await streamDisposed.Task;
        }

        private async Task OnUpload(byte[] data)
        {
            using var uploadStream = await GetUserStream();
            await OnRequestReceived(data, uploadStream);
        }

        private async Task<NestedStream> GetUserStream()
        {
            var lengthBytes = await Network.ReadBuffer(sizeof(long), default);
            var userStreamLength = BitConverter.ToInt64(lengthBytes, 0);
            return new NestedStream(Network, userStreamLength);
        }

        private byte[] Serialize(object value) => _serializer.SerializeToBytes(value);
        private T Deserialize<T>(byte[] data) => _serializer.Deserialize<T>(data);
        private Task OnRequestReceived(byte[] data, Stream userStream = null) => RequestReceived(Deserialize<Request>(data), userStream);
        private void OnResponseReceived(byte[] data, Stream userStream = null)
        {
            var response = Deserialize<Response>(data);
            response.UserStream = userStream;
            Logger?.LogInformation($"Received response for request {response.RequestId} {Name}.");
            if (_requests.TryGetValue(response.RequestId, out var completionSource))
            {
                completionSource.TrySetResult(response);
            }
        }
    }
}