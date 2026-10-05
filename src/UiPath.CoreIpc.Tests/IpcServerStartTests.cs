using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using UiPath.Ipc.Extensions.Abstractions;
using UiPath.Ipc.Transport.NamedPipe;

namespace UiPath.Ipc.Tests;

public sealed class IpcServerStartTests
{
    [Fact]
    public async Task Start_CreatesEveryFirstSlotOnItsOwnThreadBeforeReturning()
    {
        var transport = new RecordingTransport { ConcurrentAccepts = 3 };
        await using var server = new IpcServer { Transport = transport, Endpoints = new() };

        server.Start();

        transport.SlotThreads.Count.ShouldBe(3);
        transport.SlotThreads.ShouldAllBe(threadId => threadId == Environment.CurrentManagedThreadId);
    }

    [Fact]
    public async Task Start_FailingToCreateASlot_ThrowsAndDisposesTheSlotsAlreadyCreated()
    {
        var transport = new RecordingTransport { ConcurrentAccepts = 3, FailOnSlot = 3 };
        await using var server = new IpcServer { Transport = transport, Endpoints = new() };

        Should.Throw<InvalidOperationException>(server.Start).Message.ShouldBe("slot 3");

        transport.DisposedSlots.ShouldBe(2);
        transport.StateDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Start_TheNamedPipeExistsWhenStartReturns()
    {
        var pipeName = $"ipctest_{Guid.NewGuid():N}";
        await using var server = new IpcServer { Transport = new NamedPipeServerTransport { PipeName = pipeName }, Endpoints = new() };

        server.Start();

        // Neither check connects, so it cannot use up the pipe instance it is looking for.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Directory.GetFiles(@"\\.\pipe\").ShouldContain(path => path.EndsWith(pipeName, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            File.Exists(Path.Combine(Path.GetTempPath(), $"CoreFxPipe_{pipeName}")).ShouldBeTrue();
        }
    }

    private sealed class RecordingTransport : ServerTransportBase
    {
        public int? FailOnSlot { get; init; }
        public ConcurrentQueue<int> SlotThreads { get; } = new();
        public int DisposedSlots => _disposedSlots;
        public bool StateDisposed { get; private set; }

        private int _createdSlots;
        private int _disposedSlots;

        protected override ServerState CreateState() => new State(this);

        protected override IEnumerable<string?> Validate() => [];

        private sealed class State(RecordingTransport transport) : ServerState
        {
            public override ServerConnectionSlot CreateServerConnectionSlot()
            {
                var number = Interlocked.Increment(ref transport._createdSlots);
                if (number == transport.FailOnSlot)
                {
                    throw new InvalidOperationException($"slot {number}");
                }

                transport.SlotThreads.Enqueue(Environment.CurrentManagedThreadId);
                return new Slot(transport);
            }

            public override ValueTask DisposeAsync()
            {
                transport.StateDisposed = true;
                return default;
            }
        }

        private sealed class Slot(RecordingTransport transport) : ServerConnectionSlot
        {
            public override async ValueTask<Stream> AwaitConnection(CancellationToken ct)
            {
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException("unreachable");
            }

            public override ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref transport._disposedSlots);
                return default;
            }
        }
    }
}
