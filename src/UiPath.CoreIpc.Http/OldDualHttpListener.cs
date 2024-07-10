using Nito.AsyncEx;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using UiPath.Ipc;
using UiPath.Ipc.Extensibility;

namespace UiPath.CoreIpc.Http;

public sealed record DualHttpListenerConfig : ListenerConfig<DualHttpListener>
{
    public required Uri Uri { get; init; }

    protected override string DebugName => "DualHttpListenerConfig";

    protected override IEnumerable<string> Validate()
    {
        if (Uri is null) { yield return $"{nameof(Uri)} is required"; }
    }
}

public sealed class DualHttpListener : Listener<DualHttpListenerConfig, DualHttpServerConnection>
{
    private readonly CancellationTokenSource _cts = new();
    private HttpListener? _listener;
    private Task? _processing;

    protected override void Initialize()
    {
        _listener = new HttpListener()
        {
            Prefixes =
            {
                Config.Uri.ToString()
            }
        };

        _listener.Start();
        _processing = ProcessContexts(_cts.Token);
    }

    private readonly ConcurrentDictionary<Guid, ConnectionStream> _streams = new();
    private readonly Channel<ConnectionStream> _newStreams = Channel.CreateUnbounded<ConnectionStream>();

    private async Task ProcessContexts(CancellationToken ct)
    {
        while (true)
        {
            var context = await _listener!.GetContextAsync();
            if (!Guid.TryParse(context.Request.Headers["X-UiPath-Ipc-ConnectionId"], out var connectionId) ||
                !Uri.TryCreate(context.Request.Headers["X-UiPath-Reverse-Uri"], UriKind.Absolute, out var reverseUri))
            {
                context.Response.StatusCode = 403;
                context.Response.Close();
                continue;
            }

            var connectionStream = _streams.GetOrAdd(connectionId, id =>
            {
                var result = new ConnectionStream(id, reverseUri);
                _ = _newStreams.Writer.TryWrite(result);
                return result;
            });
            await connectionStream.ProcessIncomming(context, ct);
        }
    }


    internal async Task<Stream> AwaitNewConnection(CancellationToken cancellationToken)
    {
        var stream = await _newStreams.Reader.ReadAsync(cancellationToken);
        return stream;
    }

    private sealed class ConnectionStream : Stream
    {
        private readonly AsyncMonitor _incommingMonitor = new();
        private readonly MemoryStream _incomming = new();
        private int _readSoFar = 0;

        private readonly AsyncMonitor _outgoingMonitor = new();
        private readonly MemoryStream _outgoing = new();

        private readonly Guid _connectionId;
        private readonly Uri _reverseUri;
        private readonly HttpClient _client;

        public ConnectionStream(Guid connectionId, Uri reverseUri)
        {
            _connectionId = connectionId;
            _reverseUri = reverseUri;
            _client = new()
            {
                BaseAddress = reverseUri,
                DefaultRequestHeaders =
                {
                    { "X-UiPath-Ipc-ConnectionId", connectionId.ToString() }
                }
            };
        }

        internal async Task ProcessIncomming(HttpListenerContext context, CancellationToken ct)
        {
            using (await _incommingMonitor.EnterAsync(ct))
            {
                await context.Request.InputStream.CopyToAsync(_incomming, ct);
                _incommingMonitor.PulseAll();
            }

            context.Response.StatusCode = 200;
            context.Response.Close();
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            using (await _incommingMonitor.EnterAsync(ct))
            {
                while (_incomming.Length <= _readSoFar)
                {
                    await _incommingMonitor.WaitAsync(ct);
                }

                var old = _incomming.Position;
                _incomming.Position = _readSoFar;
                int cBytes = _incomming.Read(buffer, offset, count);
                _readSoFar += cBytes;

                _incomming.Position = old;
                return cBytes;
            }
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            using (await _outgoingMonitor.EnterAsync(ct))
            {
                await _outgoing.WriteAsync(buffer, offset, count, ct);
                await FlushCore(ct);
            }
        }

        public override async Task FlushAsync(CancellationToken ct)
        {
            using (await _outgoingMonitor.EnterAsync(ct))
            {
                await FlushCore(ct);
            }
        }

        private async Task FlushCore(CancellationToken ct)
        {
            _outgoing.Position = 0;
            await _client.PostAsync("", new StreamContent(_outgoing), ct);
            _outgoing.SetLength(0);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }
}

public sealed class DualHttpServerConnection : ServerConnection<DualHttpListener>
{
    protected override void Initialize()
    {
    }

    public override async Task<Stream> AcceptClient(CancellationToken cancellationToken)
    {
        var stream = await Listener.AwaitNewConnection(cancellationToken);
        return stream;
    }
}

public sealed record DualHttpConnectionKey : ConnectionKey<DualHttpClientConnection>
{
    public required Uri ServerUri { get; init; }
    public required Uri ClientUri { get; init; }
}

public sealed class DualHttpClientConnection : ClientConnection<DualHttpConnectionKey>
{
    private DualHttpClientStream? _stream;

    public override bool Connected => _stream.Connected;

    protected override void Initialize()
    {
        _stream = new(this);
    }

    public override async Task<Stream> Connect(CancellationToken cancellationToken)
    => _stream!;

    private sealed class DualHttpClientStream : Stream
    {
        private Guid _connectionId = Guid.NewGuid();
        private readonly DualHttpClientConnection _connection;

        private readonly AsyncMonitor _outgoingMonitor = new();
        private readonly MemoryStream _outgoing = new();

        private readonly AsyncMonitor _incommingMonitor = new();
        private readonly MemoryStream _incomming = new();
        private int _readSoFar = 0;

        private readonly HttpClient? _client;
        private readonly HttpListener _listener;

        private readonly CancellationTokenSource _cts = new();
        private readonly Task _processing;

        private object _connectedLock = new();
        private bool _connected = false;
        public bool Connected
        {
            get
            {
                lock (_connectedLock)
                {
                    return _connected;
                }
            }
            private set
            {
                lock (_connectedLock)
                {
                    _connected = value;
                }
            }
        }

        public DualHttpClientStream(DualHttpClientConnection connection)
        {
            _connection = connection;

            _client = new()
            {
                BaseAddress = connection.ConnectionKey.ServerUri,
                DefaultRequestHeaders =
                {
                    { "X-UiPath-Ipc-ConnectionId", _connectionId.ToString() },
                    { "X-UiPath-Reverse-Uri", connection.ConnectionKey.ClientUri.ToString() }
                }
            };

            _listener = new HttpListener()
            {
                Prefixes =
                {
                    _connection.ConnectionKey.ClientUri.ToString()
                }
            };
            _listener.Start();

            _processing = ProcessAsync(_cts.Token);
        }

        public override async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try
            {
                await _processing;
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == _cts.Token)
            {
            }
        }

        private async Task ProcessAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync();
                if (context.Request.Headers["X-UiPath-Ipc-ConnectionId"] != _connectionId.ToString())
                {
                    context.Response.StatusCode = 403;
                    context.Response.Close();
                    continue;
                }

                using (await _incommingMonitor.EnterAsync(ct))
                {
                    await context.Request.InputStream.CopyToAsync(_incomming, ct);
                    context.Response.StatusCode = 200;
                    context.Response.Close();
                    _incommingMonitor.PulseAll();
                }
            }
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            using (await _incommingMonitor.EnterAsync(ct))
            {
                while (_incomming.Length <= _readSoFar)
                {
                    await _incommingMonitor.WaitAsync(ct);
                }

                var old = _incomming.Position;
                _incomming.Position = _readSoFar;
                var cBytes = _incomming.Read(buffer, offset, count);
                _readSoFar += cBytes;
                _incomming.Position = old;
                return cBytes;
            }
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            using (await _outgoingMonitor.EnterAsync(ct))
            {
                await _outgoing.WriteAsync(buffer, offset, count, ct);
                await FlushCore(ct);
            }
        }

        public override async Task FlushAsync(CancellationToken ct)
        {
            using (await _outgoingMonitor.EnterAsync(ct))
            {
                await FlushCore(ct);
            }
        }

        private async Task FlushCore(CancellationToken ct)
        {
            _outgoing.Seek(0, SeekOrigin.Begin);
            var response = await _client!.PostAsync("", new StreamContent(_outgoing), ct);
            _outgoing.SetLength(0);

            if (response.StatusCode is HttpStatusCode.OK)
            {
                Connected = true;
            }
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    }
}