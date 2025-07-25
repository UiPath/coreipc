using Microsoft.Extensions.Logging;
using UiPath.Ipc.Transport.NamedPipe;

namespace UiPath.Ipc.Tests;

public sealed partial class NamedPipeSmokeTests
{
    [Fact]
    public async Task NamedPipesShoulNotLeak()
    {
        var pipeName = $"ipctest_{Guid.NewGuid():N}";

        (await ListPipes(pipeName)).ShouldBeNullOrEmpty();

        await using (var ipcServer = CreateServer(pipeName))
        {
            var ipcClient = CreateClient(pipeName);
            var proxy = ipcClient.GetProxy<IComputingService>();

            ipcServer.Start();
            await proxy.AddFloats(2, 3).ShouldBeAsync(5);
        }

        (await ListPipes(pipeName)).ShouldBeNullOrEmpty();
    }

    private static IpcServer CreateServer(string pipeName, Action<ILoggingBuilder>? configureLogging = null)
    => new IpcServer
    {
        Transport = new NamedPipeServerTransport
        {
            PipeName = pipeName,
        },
        Endpoints = new()
        {
            typeof(IComputingService)
        },
        ServiceProvider = new ServiceCollection()
            .AddLogging(builder => configureLogging?.Invoke(builder))
            .AddSingleton<IComputingService, ComputingService>()
            .BuildServiceProvider()
    };

    private static IpcClient CreateClient(string pipeName, int? cMaxWrite = null)
    => new()
    {
        Transport = cMaxWrite is not { } value
            ? new NamedPipeClientTransport { PipeName = pipeName }
            : new BoundedWriteNamedPipeClientTransport(value) { PipeName = pipeName },
    };

    private static Task<string> ListPipes(string pattern)
    => RunPowershell($"(Get-ChildItem \\\\.\\pipe\\ | Select-Object FullName) | Where-Object {{ $_.FullName -like '{pattern}' }}");

    private static async Task<string> RunPowershell(string command)
    {
        var process = new Process
        {
            StartInfo = new()
            {
                FileName = "powershell",
                Arguments = $"-Command \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await Task.Run(process.WaitForExit);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to run powershell. ExitCode: {process.ExitCode}. Error: {error}");
        }
        return output;
    }

    private sealed record BoundedWriteNamedPipeClientTransport(int cMax) : NamedPipeClientTransport
    {
        internal override IClientState CreateState() => new State(cMax);

        private sealed class State(int cMax) : NamedPipeClientState
        {
            private BoundedWriteStream? _bounded;

            public override Stream? Network => _bounded;

            public override async ValueTask Connect(IpcClient client, CancellationToken ct)
            {
                await base.Connect(client, ct);
                _bounded = new(_pipe!, cMax);
            }
        }

        private sealed class BoundedWriteStream : Stream
        {
            private readonly Stream _target;
            private int _cRemaining;

            public BoundedWriteStream(Stream target, int cMax)
            {
                _target = target;
                _cRemaining = cMax;
            }

            public override bool CanRead => _target.CanRead;
            public override bool CanSeek => _target.CanSeek;
            public override bool CanWrite => _target.CanWrite;
            public override long Length => _target.Length;
            public override long Position { get => _target.Position; set => _target.Position = value; }
            public override void Flush() => _target.Flush();

            public override int Read(byte[] buffer, int offset, int count) => _target.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _target.Seek(offset, origin);
            public override void SetLength(long value) => _target.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count)
            {
                try
                {
                    var cActual = Math.Min(count, _cRemaining);
                    if (cActual is 0)
                    {
                        return;
                    }

                    _cRemaining -= cActual;
                    _target.Write(buffer, offset, cActual);
                }
                finally
                {
                    if (_cRemaining is 0)
                    {
                        _target.Flush();
                    }
                }
            }

            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                try
                {
                    var cActual = Math.Min(count, _cRemaining);
                    if (cActual is 0)
                    {
                        return;
                    }

                    _cRemaining -= cActual;
                    await _target.WriteAsync(buffer, offset, cActual);
                }
                finally
                {
                    if (_cRemaining is 0)
                    {
                        await _target.FlushAsync(cancellationToken);
                    }
                }
            }
        }
    }
}
