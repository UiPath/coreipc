﻿using Nito.AsyncEx;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Threading.Channels;

namespace UiPath.Ipc.Extensions.BidirectionalHttp;

using static Constants;

public sealed partial class BidiHttpServerTransport : ServerTransportBase
{
    public required Uri Uri { get; set; }

    protected override ServerState CreateState()
    => new BidiHttpServerState(this);

    protected override IEnumerable<string?> Validate() => [];

    private sealed class BidiHttpServerState : ServerState
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly HttpListener _httpListener;
        private readonly Task _processing;
        private readonly Lazy<Task> _disposing;

        private readonly ConcurrentDictionary<Guid, Channel<HttpListenerContext>> _connections = new();
        private readonly Channel<(Guid connectionId, Uri reverseUri)> _newConnections = Channel.CreateUnbounded<(Guid connectionId, Uri reverseUri)>();

        public ChannelReader<(Guid connectionId, Uri reverseUri)> NewConnections => _newConnections.Reader;
        public ChannelReader<HttpListenerContext> GetConnectionChannel(Guid connectionId) => _connections[connectionId];

        public BidiHttpServerState(BidiHttpServerTransport transport)
        {
            _httpListener = new HttpListener()
            {
                Prefixes =
            {
                transport.Uri.ToString()
            }
            };
            _processing = ProcessContexts();
            _disposing = new(DisposeCore);
        }

        public override ValueTask DisposeAsync() => new(_disposing.Value);

        private async Task DisposeCore()
        {
            _cts.Cancel();
            try
            {
                await _processing;
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == _cts.Token)
            {
            }

            foreach (var pair in _connections)
            {
                pair.Value.Writer.Complete();
            }
            _cts.Dispose();
        }

        private async Task ProcessContexts()
        {
            await foreach (var (context, connectionId, reverseUri) in AwaitContexts())
            {
                var connectionChannel = _connections.GetOrAdd(connectionId, _ =>
                {
                    _newConnections.Writer.TryWrite((connectionId, reverseUri));
                    return Channel.CreateUnbounded<HttpListenerContext>();
                });

                await connectionChannel.Writer.WriteAsync(context, _cts.Token);
            }

            async IAsyncEnumerable<(HttpListenerContext context, Guid connectionId, Uri reverseUri)> AwaitContexts()
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var context = await _httpListener.GetContextAsync();

                    if (!TryAcceptContext(context, out var connectionId, out var reverseUri))
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                        continue;
                    }

                    yield return (context, connectionId, reverseUri);
                }
            }

            bool TryAcceptContext(HttpListenerContext context, out Guid connectionId, [NotNullWhen(returnValue: true)] out Uri? reverseUri)
            {
                if (!Guid.TryParse(context.Request.Headers[ConnectionIdHeader], out connectionId) ||
                    !Uri.TryCreate(context.Request.Headers[ReverseUriHeader], UriKind.Absolute, out reverseUri))
                {
                    connectionId = Guid.Empty;
                    reverseUri = null;
                    return false;
                }

                return true;
            }
        }

        public override ServerConnectionSlot CreateServerConnectionSlot() => new BidiHttpServerConnectionSlot(this);

    }

    private sealed class BidiHttpServerConnectionSlot : ServerConnectionSlot
    {
        private readonly Pipe _pipe = new();
        private readonly BidiHttpServerState _serverState;
        private readonly Lazy<Task> _disposing;
        private readonly CancellationTokenSource _cts = new();
        private readonly AsyncLock _lock = new();
        private (Guid connectionId, Uri reverseUri)? _connection = null;
        private HttpClient? _client;
        private Task? _processing = null;

        public BidiHttpServerConnectionSlot(BidiHttpServerState serverState)
        {
            _serverState = serverState;
            _disposing = new(DisposeCore);
        }

        private async Task DisposeCore()
        {
            _cts.Cancel();

            _client?.Dispose();

            try
            {
                await (_processing ?? Task.CompletedTask);
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == _cts.Token)
            {
                // ignored
            }

            _cts.Dispose();
        }

        public override async ValueTask<Stream> AwaitConnection(CancellationToken ct)
        {
            using (await _lock.LockAsync(ct))
            {
                if (_connection is not null)
                {
                    throw new InvalidOperationException();
                }

                _connection = await _serverState.NewConnections.ReadAsync(ct);

                _client = new()
                {
                    BaseAddress = _connection.Value.reverseUri,
                    DefaultRequestHeaders =
                    {
                        { ConnectionIdHeader, _connection.Value.connectionId.ToString() }
                    }
                };

                _processing = ProcessContexts(_cts.Token);

                return new Adapter(this);
            }
        }

        public override ValueTask DisposeAsync() => new(_disposing.Value);

        private async Task ProcessContexts(CancellationToken ct)
        {
            var reader = _serverState.GetConnectionChannel(_connection!.Value.connectionId);

            while (await reader.WaitToReadAsync(ct))
            {
                if (!reader.TryRead(out var context))
                {
                    continue;
                }
                await ProcessContext(context);
            }

            async Task ProcessContext(HttpListenerContext context)
            {
                try
                {
                    while (true)
                    {
                        var memory = _pipe.Writer.GetMemory();
                        var cbRead = await context.Request.InputStream.ReadAsync(memory, ct);
                        if (cbRead is 0)
                        {
                            break;
                        }
                        _pipe.Writer.Advance(cbRead);
                        var flushResult = await _pipe.Writer.FlushAsync(ct);
                        if (flushResult.IsCompleted)
                        {
                            break;
                        }
                    }
                }
                finally
                {
                    context.Response.StatusCode = 200;
                    context.Response.Close();
                }
            }
        }

        private sealed class Adapter : Stream
        {
            private readonly BidiHttpServerConnectionSlot _slot;

            public Adapter(BidiHttpServerConnectionSlot slot)
            {
                _slot = slot;
            }

            public
#if !NET461
            override
#endif
            ValueTask DisposeAsync() => _slot.DisposeAsync();

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                var memory = new Memory<byte>(buffer, offset, count);
                var readResult = await _slot._pipe.Reader.ReadAsync(ct);

                var take = (int)Math.Min(readResult.Buffer.Length, memory.Length);

                readResult.Buffer.Slice(start: 0, length: take).CopyTo(memory.Span);
                _slot._pipe.Reader.AdvanceTo(readResult.Buffer.GetPosition(take));

                return take;
            }

            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                var memory = new ReadOnlyMemory<byte>(buffer, offset, count);
                if (_slot._client is null)
                {
                    throw new InvalidOperationException();
                }

                HttpContent content =
#if NET461
        new ByteArrayContent(memory.ToArray());
#else
                new ReadOnlyMemoryContent(memory);
#endif

                await _slot._client.PostAsync(requestUri: "", content, ct);
            }

            public override Task FlushAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

            public override void Flush() => throw new NotImplementedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();
            public override void SetLength(long value) => throw new NotImplementedException();
            public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();

            public override long Length => throw new NotImplementedException();
            public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        }
    }
}
