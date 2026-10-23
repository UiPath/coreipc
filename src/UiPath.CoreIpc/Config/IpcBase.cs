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
    /// The <see cref="Exception.Data"/> keys whose <see cref="string"/> values (other values are skipped) are sent along with the errors of the calls
    /// this endpoint handles, at every level of the inner exception chain, landing in the caller's <see cref="RemoteException"/>.<see cref="Exception.Data"/>.
    /// <c>null</c> or empty (the default) sends nothing. It is read when a connection is established.
    /// </summary>
    public IReadOnlyCollection<string>? PropagatedExceptionDataKeys { get; set; }
}
