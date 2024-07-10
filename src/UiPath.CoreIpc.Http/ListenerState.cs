using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading.Channels;
using UiPath.Ipc;

namespace UiPath.CoreIpc.Http;

partial class BidirectionalHttp
{
    public sealed class ListenerState : IAsyncDisposable
    {
        private readonly IpcServer _server;
        private readonly ListenerConfig _config;
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _processing;

        private readonly ConcurrentDictionary<Guid, ServerConnectionState> _connections = new();
        private readonly Channel<ServerConnectionState> _newConnections = Channel.CreateUnbounded<ServerConnectionState>();

        public ChannelReader<ServerConnectionState> NewConnections => _newConnections.Reader;

        public ListenerState(IpcServer server, ListenerConfig config)
        {
            _server = server;
            _config = config;

            _listener = new HttpListener()
            {
                Prefixes =
                {
                    _config.Uri.ToString()
                }
            };

            _listener.Start();
            _processing = ProcessContexts();
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try
            {
                await _processing;
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken == _cts.Token)
            {
            }

            var remainingConnections = _newConnections.Reader.ReadAllAsync().ToBlockingEnumerable().ToArray();
            foreach (var connection in remainingConnections)
            {
                await connection.DisposeAsync();
            }
        }

        private async Task ProcessContexts()
        {
            await foreach (var (context, connectionId, reverseUri) in AwaitContexts())
            {
                var connection = _connections.GetOrAdd(connectionId, CreateConnection, reverseUri);
                await connection.ProcessContext(context, _cts.Token);

                ServerConnectionState CreateConnection(Guid id, Uri reverseUri)
                {
                    var newConnection = new ServerConnectionState(_config, this, id, reverseUri);
                    _ = _newConnections.Writer.TryWrite(newConnection);
                    return newConnection;
                }
            }

            async IAsyncEnumerable<(HttpListenerContext context, Guid connectionId, Uri reverseUri)> AwaitContexts()
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync();

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
}
