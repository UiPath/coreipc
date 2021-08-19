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
        private long _requestCounter = -1;
        private readonly int _maxMessageSize;
        private readonly Lazy<Task> _receiveLoop;
        private readonly AsyncLock _sendLock = new();
        private readonly Action<object> _onResponse;
        private readonly Action<object> _onRequest;
        private readonly Action<object> _onCancellation;
        public Connection(Stream network, ISerializer serializer, ILogger logger, string name, int maxMessageSize = int.MaxValue)
        {
            Network = network;
            Serializer = serializer;
            Logger = logger;
            Name = $"{name} {GetHashCode()}";
            _maxMessageSize = maxMessageSize;
            _receiveLoop = new(ReceiveLoop);
            _onResponse = OnResponseReceived;
            _onRequest = OnRequestReceived;
            _onCancellation = OnCancellationReceived;
        }
        public Stream Network { get; }
        public ILogger Logger { get; internal set; }
        public string Name { get; }
        public ISerializer Serializer { get; }
        public override string ToString() => Name;
        public string NewRequestId() => Interlocked.Increment(ref _requestCounter).ToString();
        public Task Listen() => _receiveLoop.Value;
        internal event Func<Request, Stream, Task> RequestReceived;
        internal event Action<string> CancellationRequestReceived;
        public event EventHandler<EventArgs> Closed;
        internal async Task<Response> RemoteCall(Request request, Stream uploadStream, CancellationToken token)
        {
            var requestBytes = await SerializeToStream(request);
            var requestCompletion = new RequestCompletionSource();
            _requests[request.Id] = requestCompletion;
            try
            {
                await SendRequest(requestBytes, uploadStream, token);
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
                if (uploadStream == null)
                {
                    CancelServerCall(request.Id).LogException(Logger, this);
                }
                else
                {
                    Dispose();
                }
                requestCompletion.TrySetCanceled();
            }
            async Task CancelServerCall(string requestId) => 
                await SendMessage(MessageType.CancellationRequest, await SerializeToStream(new CancellationRequest(requestId)), default);
        }
        private Task SendRequest(MemoryStream requestBytes, Stream uploadStream, CancellationToken cancellationToken) => uploadStream == null ?
                SendMessage(MessageType.Request, requestBytes, cancellationToken) :
                SendStream(new(MessageType.UploadRequest, requestBytes), uploadStream, cancellationToken);
        internal async Task Send(Response response, CancellationToken cancellationToken)
        {
            var responseBytes = await SerializeToStream(response);
            var downloadStream = response.DownloadStream;
            if (downloadStream == null)
            {
                await SendMessage(MessageType.Response, responseBytes, cancellationToken);
                return;
            }
            using (downloadStream)
            {
                await SendStream(new(MessageType.DownloadResponse, responseBytes), downloadStream, cancellationToken);
            }
        }
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
        private Task SendMessage(MessageType messageType, MemoryStream data, CancellationToken cancellationToken) => 
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
            var closedHandler = Interlocked.CompareExchange(ref Closed, null, Closed);
            if (closedHandler == null)
            {
                return;
            }
            Network.Dispose();
            try
            {
                closedHandler.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, this);
            }
            CompleteRequests();
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
            Task HandleMessage(WireMessage message)
            {
                var data = message.Data;
                Action<object> callback = null;
                switch (message.MessageType)
                {
                    case MessageType.Response:
                        callback = _onResponse;
                        break;
                    case MessageType.Request when RequestReceived != null:
                        callback = _onRequest;
                        break;
                    case MessageType.CancellationRequest when CancellationRequestReceived != null:
                        callback = _onCancellation;
                        break;
                    case MessageType.UploadRequest:
                        return OnUploadRequest(data);
                    case MessageType.DownloadResponse:
                        return OnDownloadResponse(data);
                    default:
                        Logger?.LogInformation("Unknown message type " + message.MessageType);
                        break;
                };
                if (callback != null)
                {
                    Task.Factory.StartNew(callback, data, default, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default).LogException(Logger, this);
                }
                return Task.CompletedTask;
            }
        }
        private async Task OnDownloadResponse(Stream data)
        {
            var downloadStream = await WrapNetworkStream();
            var streamDisposed = new TaskCompletionSource<bool>();
            downloadStream.Disposed += delegate { streamDisposed.TrySetResult(true); };
            OnResponseReceived(data, downloadStream);
            await streamDisposed.Task;
        }
        private async Task OnUploadRequest(Stream data)
        {
            using var uploadStream = await WrapNetworkStream();
            await OnRequestReceived(data, uploadStream);
        }
        private async Task<NestedStream> WrapNetworkStream()
        {
            var lengthBytes = await Network.ReadBuffer(sizeof(long), default);
            var userStreamLength = BitConverter.ToInt64(lengthBytes, 0);
            return new NestedStream(Network, userStreamLength);
        }
        private async Task<MemoryStream> SerializeToStream(object value)
        {
            var stream = new MemoryStream { Position = IOHelpers.HeaderLength };
            await Serializer.Serialize(value, stream);
            return stream;
        }
        private T Deserialize<T>(Stream data) => Serializer.Deserialize<T>(data);
        private void OnRequestReceived(object data) => OnRequestReceived((Stream)data, null);
        private Task OnRequestReceived(Stream data, Stream uploadStream) => RequestReceived(Deserialize<Request>(data), uploadStream);
        private void OnResponseReceived(object data) => OnResponseReceived((Stream)data, null);
        private void OnResponseReceived(Stream data, Stream downloadStream)
        {
            var response = Deserialize<Response>(data);
            response.DownloadStream = downloadStream;
            Logger?.LogInformation($"Received response for request {response.RequestId} {Name}.");
            if (_requests.TryGetValue(response.RequestId, out var completionSource))
            {
                completionSource.TrySetResult(response);
            }
        }
        private void OnCancellationReceived(object data) => CancellationRequestReceived(Deserialize<CancellationRequest>((Stream)data).RequestId);
    }
}