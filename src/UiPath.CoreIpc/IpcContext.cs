using System;
using System.Threading;

namespace UiPath.Ipc;

/// <summary>Ambient context of the IPC call being honored, letting a POCO contract reach
/// the peer without a <see cref="Message"/> parameter. Coexists with <see cref="Message"/>.</summary>
public sealed class IpcContext
{
    private static readonly AsyncLocal<IpcContext?> CurrentContext = new();

    /// <summary>The call in progress on the current async flow, or null if there is none.</summary>
    public static IpcContext? Current => CurrentContext.Value;

    internal IpcContext(IClient? client, CancellationToken cancellationToken)
    {
        Client = client;
        CancellationToken = cancellationToken;
    }

    /// <summary>The peer of the in-flight call (same handle as <see cref="Message.Client"/>),
    /// or null when the current endpoint has no reachable peer.</summary>
    public IClient? Client { get; }

    /// <summary>The cancellation token of the in-flight call.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Equivalent to <c>Message.Client.GetCallback&lt;TCallback&gt;()</c>.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Client"/> is null.</exception>
    public TCallback GetCallback<TCallback>() where TCallback : class
    => (Client ?? throw new InvalidOperationException(
            $"{nameof(IpcContext)}.{nameof(Current)} has no peer client; " +
            $"{nameof(GetCallback)} is only available while honoring a server-side IPC call."))
        .GetCallback<TCallback>();

    /// <summary>Publishes <paramref name="context"/> as <see cref="Current"/> for the returned
    /// scope, restoring the previous value on dispose so nested calls compose.</summary>
    internal static IDisposable Push(IpcContext context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly IpcContext? _previous;
        private bool _disposed;

        public Scope(IpcContext? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            CurrentContext.Value = _previous;
        }
    }
}
