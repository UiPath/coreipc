using Nito.AsyncEx;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Threading.Channels;

namespace UiPath.Ipc.Http;

using static Constants;
using IBidiHttpListenerConfig = IListenerConfig<BidiHttpListener, BidiHttpListenerState, BidiHttpServerConnectionState>;

public sealed partial record BidiHttpListener : ListenerConfig, IBidiHttpListenerConfig
{
    public required Uri Uri { get; init; }

    BidiHttpListenerState IBidiHttpListenerConfig.CreateListenerState(IpcServer server)
    => new(server, this);

    BidiHttpServerConnectionState IBidiHttpListenerConfig.CreateConnectionState(IpcServer server, BidiHttpListenerState listenerState)
    => new(server, listenerState);

    async ValueTask<Network> IBidiHttpListenerConfig.AwaitConnection(BidiHttpListenerState listenerState, BidiHttpServerConnectionState connectionState, CancellationToken ct)
    {
        await connectionState.WaitForConnection(ct);
        return connectionState;
    }

    public IEnumerable<string> Validate()
    {
        throw new NotImplementedException();
    }
}

internal sealed class BidiHttpListenerState : IAsyncDisposable
{
    private readonly IpcServer _ipcServer;
    private readonly CancellationTokenSource _cts = new();
    private readonly HttpListener _httpListener;
    private readonly Task _processing;
    private readonly Lazy<Task> _disposing;

    private readonly ConcurrentDictionary<Guid, Channel<HttpListenerContext>> _connections = new();
    private readonly Channel<(Guid connectionId, Uri reverseUri)> _newConnections = Channel.CreateUnbounded<(Guid connectionId, Uri reverseUri)>();

    public ChannelReader<(Guid connectionId, Uri reverseUri)> NewConnections => _newConnections.Reader;
    public ChannelReader<HttpListenerContext> GetConnectionChannel(Guid connectionId) => _connections[connectionId];

    public BidiHttpListenerState(IpcServer ipcServer, BidiHttpListener listener)
    {
        _ipcServer = ipcServer;
        _httpListener = new HttpListener()
        {
            Prefixes =
            {
                listener.Uri.ToString()
            }
        };
        _processing = ProcessContexts();
        _disposing = new(DisposeCore);
    }

    public ValueTask DisposeAsync() => new(_disposing.Value);

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
}

internal sealed class BidiHttpServerConnectionState : IAsyncDisposable, IAsyncStream
{
    private readonly Pipe _pipe = new();

    private readonly IpcServer _server;
    private readonly BidiHttpListenerState _listenerState;

    private readonly CancellationTokenSource _cts = new();
    private readonly AsyncLock _lock = new();
    private (Guid connectionId, Uri reverseUri)? _connection = null;
    private HttpClient? _client;
    private Task? _processing = null;
    private readonly Lazy<Task> _disposing;

    public BidiHttpServerConnectionState(IpcServer server, BidiHttpListenerState listenerState)
    {
        _server = server;
        _listenerState = listenerState;
        _disposing = new(DisposeCore);
    }

    public ValueTask DisposeAsync() => new(_disposing.Value);

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

    public async Task WaitForConnection(CancellationToken ct)
    {
        using (await _lock.LockAsync(ct))
        {
            if (_connection is not null)
            {
                throw new InvalidOperationException();
            }

            _connection = await _listenerState.NewConnections.ReadAsync(ct);

            _client = new()
            {
                BaseAddress = _connection.Value.reverseUri,
                DefaultRequestHeaders =
                {
                    { ConnectionIdHeader, _connection.Value.connectionId.ToString() }
                }
            };

            _processing = ProcessContexts(_cts.Token);
        }
    }

    private async Task ProcessContexts(CancellationToken ct)
    {
        var reader = _listenerState.GetConnectionChannel(_connection!.Value.connectionId);        

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

    async ValueTask<int> IAsyncStream.Read(Memory<byte> memory, CancellationToken ct)
    {
        var readResult = await _pipe.Reader.ReadAsync(ct);

        var take = (int)Math.Min(readResult.Buffer.Length, memory.Length);

        readResult.Buffer.Slice(start: 0, length: take).CopyTo(memory.Span);
        _pipe.Reader.AdvanceTo(readResult.Buffer.GetPosition(take));

        return take;
    }

    async ValueTask IAsyncStream.Write(ReadOnlyMemory<byte> memory, CancellationToken ct)
    {
        if (_client is null)
        {
            throw new InvalidOperationException();
        }

        HttpContent content =
#if NET461
        new ByteArrayContent(memory.ToArray());
#else
        new ReadOnlyMemoryContent(memory);
#endif

        await _client.PostAsync(requestUri: "", content, ct);
    }

    ValueTask IAsyncStream.Flush(CancellationToken ct) => default;
}
