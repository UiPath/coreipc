namespace UiPath.Ipc;

public abstract class IpcBase
{
    /// <summary>
    /// The optional default timeout for all invocation requests. Leaving it or explicitly setting it to <c>null</c> has the same effect as setting it to <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </summary>
    public TimeSpan? RequestTimeout { get; set; }
    public IServiceProvider? ServiceProvider { get; set; }
    public TaskScheduler? Scheduler { get; set; }
    /// <summary>
    /// The <see cref="Exception.Data"/> keys whose values are sent along with the errors of the calls this endpoint handles,
    /// at every level of the inner exception chain, landing in the caller's <see cref="RemoteException"/>.<see cref="Exception.Data"/>.
    /// Each value must be a string, opaque to CoreIpc; put a structured payload inside it (e.g. as JSON). <c>null</c> or empty (the default) sends nothing of this endpoint's own.
    /// </summary>
    /// <remarks>
    /// The entries a <see cref="RemoteException"/> received travel on with it, listed or not, when it fails a call this endpoint handles,
    /// whether rethrown as is or as an inner exception.
    /// </remarks>
    public IReadOnlyCollection<string>? PropagatedExceptionDataKeys { get; set; }
}
