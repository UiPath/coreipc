using Nito.AsyncEx;
using System.Net;
using UiPath.Ipc.Extensibility;

namespace UiPath.CoreIpc.Http;

partial class BidirectionalHttp
{
    public sealed class ClientConnection : ClientConnection<ConnectionKey>
    {
        private ClientStream? _stream;

        public override bool Connected => _stream!.Connected;

        protected override void Initialize()
        {
            _stream = new(this);
        }

        public override Task<Stream> Connect(CancellationToken cancellationToken)
        => Task.FromResult<Stream>(_stream!);

        private sealed class ClientStream : Stream
        {
            private Guid _connectionId = Guid.NewGuid();
            private readonly ClientConnection _connection;

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

            public ClientStream(ClientConnection connection)
            {
                _connection = connection;

                _client = new()
                {
                    BaseAddress = connection.ConnectionKey.ServerUri,
                    DefaultRequestHeaders =
                {
                    { ConnectionIdHeader, _connectionId.ToString() },
                    { ReverseUriHeader, connection.ConnectionKey.ClientUri.ToString() }
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
                    if (context.Request.Headers[ConnectionIdHeader] != _connectionId.ToString())
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
}
