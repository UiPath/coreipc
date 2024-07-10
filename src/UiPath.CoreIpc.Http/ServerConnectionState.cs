using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using UiPath.Ipc;

namespace UiPath.CoreIpc.Http;

partial class BidirectionalHttp
{
    public sealed class ServerConnectionState : IAsyncStream, IAsyncDisposable
    {
        private readonly ListenerConfig _config;
        private readonly ListenerState _listener;
        private readonly Guid _connectionId;
        private readonly Uri _reverseUri;
        private readonly HttpClient _client;

        private readonly Pipe _pipe = new();

        internal ServerConnectionState(ListenerConfig config, ListenerState listener, Guid connectionId, Uri reverseUri)
        {
            _config = config;
            _listener = listener;
            _connectionId = connectionId;
            _reverseUri = reverseUri;
            _client = new()
            {
                BaseAddress = _reverseUri,
                DefaultRequestHeaders =
                {
                    { ConnectionIdHeader, _connectionId.ToString() }
                }
            };
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
        }

        internal async Task ProcessContext(HttpListenerContext context, CancellationToken ct = default)
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

        async ValueTask<int> IAsyncStream.Read(Memory<byte> memory, CancellationToken ct)
        {
            var readResult = await _pipe.Reader.ReadAsync(ct);

            var take = (int)Math.Min(readResult.Buffer.Length, memory.Length);

            readResult.Buffer.Slice(start: 0, length: take).CopyTo(memory.Span);
            _pipe.Reader.AdvanceTo(readResult.Buffer.GetPosition(take));

            return take;
        }

        async ValueTask IAsyncStream.Write(ReadOnlyMemory<byte> memory, CancellationToken ct)
        => await _client.PostAsync(
            requestUri: "",
            new ReadOnlyMemoryContent(memory),
            ct);

        ValueTask IAsyncStream.Flush(CancellationToken ct) => default;
    }
}
