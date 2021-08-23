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
        private readonly Action<object> _cancelRequest;
        private readonly Action<object> _cancelUploadRequest;
        public Connection(Stream network, ISerializer serializer, ILogger logger, string name, int maxMessageSize = int.MaxValue)
        {
            Network = network;
            Serializer = serializer;
            Logger = logger;
            Name = $"{name} {GetHashCode()}";
            _maxMessageSize = maxMessageSize;
            _receiveLoop = new(ReceiveLoop);
            _onResponse = data => OnResponseReceived((Stream)data, null);
            _onRequest = data => OnRequestReceived((Stream)data, null);
            _onCancellation = data => OnCancellationReceived((Stream)data);
            _cancelRequest = requestId => CancelRequest((string)requestId);
            _cancelUploadRequest = requestId => CancelUploadRequest((string)requestId);
        }
        public Stream Network { get; }
        public ILogger Logger { get; internal set; }
        public bool LogEnabled => Logger.Enabled();
        public string Name { get; }
        public ISerializer Serializer { get; }
        public override string ToString() => Name;
        public string NewRequestId() => Interlocked.Increment(ref _requestCounter).ToString();
        public Task Listen() => _receiveLoop.Value;
        internal event Func<Request, Stream, Task> RequestReceived;
        internal event Action<string> CancellationReceived;
        public event EventHandler<EventArgs> Closed;
        internal async Task<Response> RemoteCall(Request request, Stream uploadStream, CancellationToken token)
        {
            var requestBytes = SerializeToStream(request);
            var requestCompletion = new RequestCompletionSource();
            _requests[request.Id] = requestCompletion;
            CancellationTokenRegistration tokenRegistration = default;
            try
            {
                tokenRegistration = token.Register(uploadStream == null ? _cancelRequest : _cancelUploadRequest, request.Id);
                await SendRequest(requestBytes, uploadStream, token);
                return await requestCompletion.Task;
            }
            finally
            {
                tokenRegistration.Dispose();
                _requests.TryRemove(request.Id, out _);
            }
        }
        void CancelRequest(string requestId)
        {
            CancelServerCall(requestId).LogException(Logger, this);
            TryCancelRequest(requestId);
            return;
            Task CancelServerCall(string requestId) =>
                SendMessage(MessageType.CancellationRequest, SerializeToStream(new CancellationRequest(requestId)), default);
        }
        void CancelUploadRequest(string requestId)
        {
            Dispose();
            TryCancelRequest(requestId);
        }
        private void TryCancelRequest(string requestId)
        {
            if (_requests.TryGetValue(requestId, out var requestCompletion))
            {
                requestCompletion.TrySetCanceled();
            }
        }
        private Task SendRequest(MemoryStream requestBytes, Stream uploadStream, CancellationToken cancellationToken) => uploadStream == null ?
                SendMessage(MessageType.Request, requestBytes, cancellationToken) :
                SendStream(new(MessageType.UploadRequest, requestBytes), uploadStream, cancellationToken);
        internal Task Send(Response response, CancellationToken cancellationToken) => response.DownloadStream == null ? 
                SendMessage(MessageType.Response, SerializeToStream(response), cancellationToken) : 
                SendDownloadStream(SerializeToStream(response), response.DownloadStream, cancellationToken);
        private async Task SendDownloadStream(MemoryStream responseBytes, Stream downloadStream, CancellationToken cancellationToken)
        {
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
            SendMessage(new(messageType, data), cancellationToken);
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
            if (LogEnabled)
            {
                Log($"{nameof(ReceiveLoop)} {Name} finished.");
            }
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
                    case MessageType.CancellationRequest when CancellationReceived != null:
                        callback = _onCancellation;
                        break;
                    case MessageType.UploadRequest:
                        return OnUploadRequest(data);
                    case MessageType.DownloadResponse:
                        return OnDownloadResponse(data);
                    default:
                        if (LogEnabled)
                        {
                            Log("Unknown message type " + message.MessageType);
                        }
                        break;
                };
                if (callback != null)
                {
                    Task.Factory.StartNew(callback, data, default, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
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
        private async ValueTask<NestedStream> WrapNetworkStream()
        {
            var lengthBytes = await Network.ReadBufferCore(sizeof(long), default);
            if (lengthBytes.Length == 0)
            {
                throw new IOException("Connection closed.");
            }
            var userStreamLength = BitConverter.ToInt64(lengthBytes, 0);
            return new NestedStream(Network, userStreamLength);
        }
        private MemoryStream SerializeToStream(object value)
        {
            var stream = new MemoryStream { Position = IOHelpers.HeaderLength };
            Serializer.Serialize(value, stream);
            return stream;
        }
        private T Deserialize<T>(Stream data) => Serializer.Deserialize<T>(data);
        private void OnCancellationReceived(Stream data)
        {
            try
            {
                CancellationReceived(Deserialize<CancellationRequest>(data).RequestId);
            }
            catch(Exception ex)
            {
                Log(ex);
            }
        }
        private void Log(Exception ex) => Logger.LogException(ex, Name);
        private Task OnRequestReceived(Stream data, Stream uploadStream)
        {
            try
            {
                return RequestReceived(Deserialize<Request>(data), uploadStream);
            }
            catch (Exception ex)
            {
                Log(ex);
            }
            return Task.CompletedTask;
        }
        private void OnResponseReceived(Stream data, Stream downloadStream)
        {
            try
            {
                var response = Deserialize<Response>(data);
                response.DownloadStream = downloadStream;
                if (LogEnabled)
                {
                    Log($"Received response for request {response.RequestId} {Name}.");
                }
                if (_requests.TryGetValue(response.RequestId, out var completionSource))
                {
                    completionSource.TrySetResult(response);
                }
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }
        public void Log(string message) => Logger.LogInformation(message);
    }
}