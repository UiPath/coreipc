using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace UiPath.CoreIpc
{
    using RequestCompletionSource = TaskCompletionSource<Response>;
    using static IOHelpers;
    public sealed class Connection : IDisposable
    {
        private readonly ConcurrentDictionary<string, RequestCompletionSource> _requests = new();
        private long _requestCounter = -1;
        private readonly int _maxMessageSize;
        private readonly Lazy<Task> _receiveLoop;
        private readonly SemaphoreSlim _sendLock = new(1);
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
            _onResponse = data => OnResponseReceived((Response)data);
            _onRequest = data => OnRequestReceived((Request)data);
            _onCancellation = data => OnCancellationReceived((string)data);
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
        internal event Func<Request, Task> RequestReceived;
        internal event Action<string> CancellationReceived;
        public event EventHandler<EventArgs> Closed;
        internal async Task<Response> RemoteCall(Request request, CancellationToken token)
        {
            var requestCompletion = new RequestCompletionSource();
            _requests[request.Id] = requestCompletion;
            CancellationTokenRegistration tokenRegistration = default;
            try
            {
                await Send(request, token);
                tokenRegistration = token.Register(request.UploadStream == null ? _cancelRequest : _cancelUploadRequest, request.Id);
                return await requestCompletion.Task;
            }
            finally
            {
                tokenRegistration.Dispose();
                _requests.TryRemove(request.Id, out _);
            }
        }
        internal Task Send(Request request, CancellationToken token)
        {
            Debug.Assert(request.Parameters == null || request.ObjectParameters == null);
            var requestBytes = SerializeToStream(request);
            return request.UploadStream == null ?
                SendMessage(MessageType.Request, requestBytes, token) :
                SendStream(MessageType.UploadRequest, requestBytes, request.UploadStream, token);
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
        internal Task Send(Response response, CancellationToken cancellationToken)
        {
            Debug.Assert(response.Data == null || response.ObjectData == null);
            var responseBytes = SerializeToStream(response);
            return response.DownloadStream == null ?
                SendMessage(MessageType.Response, responseBytes, cancellationToken) :
                SendDownloadStream(responseBytes, response.DownloadStream, cancellationToken);
        }
        private async Task SendDownloadStream(MemoryStream responseBytes, Stream downloadStream, CancellationToken cancellationToken)
        {
            using (downloadStream)
            {
                await SendStream(MessageType.DownloadResponse, responseBytes, downloadStream, cancellationToken);
            }
        }
        private async Task SendStream(MessageType messageType, Stream data, Stream userStream, CancellationToken cancellationToken)
        {
            await _sendLock.WaitAsync(cancellationToken);
            CancellationTokenRegistration tokenRegistration = default;
            try
            {
                tokenRegistration = cancellationToken.Register(Dispose);
                await Network.WriteMessage(messageType, data, cancellationToken);
                await Network.WriteBuffer(BitConverter.GetBytes(userStream.Length), cancellationToken);
                const int DefaultCopyBufferSize = 81920;
                await userStream.CopyToAsync(Network, DefaultCopyBufferSize, cancellationToken);
            }
            finally
            {
                _sendLock.Release();
                tokenRegistration.Dispose();
            }
        }
        private async Task SendMessage(MessageType messageType, MemoryStream data, CancellationToken cancellationToken)
        {
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                await Network.WriteMessage(messageType, data, CancellationToken.None);
            }
            finally
            {
                _sendLock.Release();
            }
        }
        public void Dispose()
        {
            var closedHandler = Interlocked.CompareExchange(ref Closed, null, Closed);
            if (closedHandler == null)
            {
                return;
            }
            _sendLock.AssertDisposed();
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
            try
            {
                byte[] header;
                while ((header = await Network.ReadBuffer(HeaderLength)).Length > 0)
                {
                    var length = BitConverter.ToInt32(header, startIndex: 1);
                    if (length > _maxMessageSize)
                    {
                        throw new InvalidDataException($"Message too large. The maximum message size is {_maxMessageSize / (1024 * 1024)} megabytes.");
                    }
                    await HandleMessage((MessageType)header[0], new NestedStream(Network, length));
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
            async Task HandleMessage(MessageType messageType, Stream stream)
            {
                object state = null;
                Action<object> callback = null;
                switch (messageType)
                {
                    case MessageType.Response:
                        state = await Deserialize<Response>(stream);
                        callback = _onResponse;
                        break;
                    case MessageType.Request:
                        state = await Deserialize<Request>(stream);
                        callback = _onRequest;
                        break;
                    case MessageType.CancellationRequest:
                        state = (await Deserialize<CancellationRequest>(stream)).RequestId;
                        callback = _onCancellation;
                        break;
                    case MessageType.UploadRequest:
                        await OnUploadRequest(stream);
                        return;
                    case MessageType.DownloadResponse:
                        await OnDownloadResponse(stream);
                        return;
                    default:
                        if (LogEnabled)
                        {
                            Log("Unknown message type " + messageType);
                        }
                        break;
                };
                if (callback != null)
                {
                    _=Task.Factory.StartNew(callback, state, default, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
                }
            }
        }
        private async Task OnDownloadResponse(Stream data)
        {
            var response = await Deserialize<Response>(data);
            var downloadStream = await WrapNetworkStream();
            var streamDisposed = new TaskCompletionSource<bool>();
            downloadStream.Disposed += delegate { streamDisposed.TrySetResult(true); };
            response.DownloadStream = downloadStream;
            OnResponseReceived(response);
            await streamDisposed.Task;
        }
        private async Task OnUploadRequest(Stream data)
        {
            var request = await Deserialize<Request>(data);
            using var uploadStream = await WrapNetworkStream();
            request.UploadStream = uploadStream;
            await OnRequestReceived(request);
        }
        private async ValueTask<NestedStream> WrapNetworkStream()
        {
            var lengthBytes = await Network.ReadBuffer(sizeof(long));
            if (lengthBytes.Length == 0)
            {
                throw new IOException("Connection closed.");
            }
            var userStreamLength = BitConverter.ToInt64(lengthBytes, 0);
            return new NestedStream(Network, userStreamLength);
        }
        private MemoryStream SerializeToStream(object value)
        {
            var stream = GetStream();
            try
            {
                stream.Position = HeaderLength;
                Serializer.Serialize(value, stream);
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        private Task<T> Deserialize<T>(Stream data) => Serializer.DeserializeAsync<T>(data);
        private void OnCancellationReceived(string requestId)
        {
            try
            {
                CancellationReceived(requestId);
            }
            catch(Exception ex)
            {
                Log(ex);
            }
        }
        private void Log(Exception ex) => Logger.LogException(ex, Name);
        private Task OnRequestReceived(Request request)
        {
            try
            {
                return RequestReceived(request);
            }
            catch (Exception ex)
            {
                Log(ex);
            }
            return Task.CompletedTask;
        }
        private void OnResponseReceived(Response response)
        {
            try
            {
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