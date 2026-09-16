using System;
using System.Threading;

namespace UiPath.Ipc;

/// <summary>Ambient options for the IPC calls made on the current async flow, letting a POCO
/// contract carry a per-call deadline without a <see cref="Message"/> parameter. The caller-side
/// counterpart of <see cref="IpcContext"/>; an explicit <see cref="Message"/> argument still wins.</summary>
public sealed class IpcCallOptions
{
    private static readonly AsyncLocal<IpcCallOptions?> CurrentOptions = new();

    /// <summary>The options in force on the current async flow, or null if there are none.</summary>
    public static IpcCallOptions? Current => CurrentOptions.Value;

    private IpcCallOptions(TimeSpan requestTimeout) => RequestTimeout = requestTimeout;

    /// <summary>How long a call may take before the caller gives up. <see cref="TimeSpan.Zero"/>
    /// means "no override", matching <see cref="Message.RequestTimeout"/>.</summary>
    public TimeSpan RequestTimeout { get; }

    /// <summary>Applies <paramref name="requestTimeout"/> to every call on this async flow until the
    /// returned scope is disposed, restoring the previous value so nested scopes compose.</summary>
    public static IDisposable With(TimeSpan requestTimeout)
    {
        var previous = CurrentOptions.Value;
        CurrentOptions.Value = new IpcCallOptions(requestTimeout);
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly IpcCallOptions? _previous;
        private bool _disposed;

        public Scope(IpcCallOptions? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            CurrentOptions.Value = _previous;
        }
    }
}
