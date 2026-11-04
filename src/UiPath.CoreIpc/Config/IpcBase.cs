namespace UiPath.Ipc;

public abstract class IpcBase
{
    /// <summary>
    /// The optional default timeout for all invocation requests. Leaving it or explicitly setting it to <c>null</c> has the same effect as setting it to <see cref="Timeout.InfiniteTimeSpan"/>.
    /// </summary>
    public TimeSpan? RequestTimeout { get; set; }
    public IServiceProvider? ServiceProvider { get; set; }
    public TaskScheduler? Scheduler { get; set; }
    private static IReadOnlyCollection<string>? _defaultPropagatedExceptionDataKeys = ["UiPath.ErrorInfo.Error"];
    private IReadOnlyCollection<string>? _propagatedExceptionDataKeys;
    /// <summary>
    /// The <see cref="Exception.Data"/> keys every server and client in this process sends, on top of its own <see cref="PropagatedExceptionDataKeys"/>.
    /// It holds <c>UiPath.ErrorInfo.Error</c> by default; <c>null</c> or empty leaves only each server's or client's own keys. Set it at startup, before any connection is made.
    /// </summary>
    public static IReadOnlyCollection<string>? DefaultPropagatedExceptionDataKeys
    {
        get => _defaultPropagatedExceptionDataKeys;
        set => _defaultPropagatedExceptionDataKeys = value?.ToArray();
    }
    /// <summary>
    /// The <see cref="Exception.Data"/> keys whose values are sent along with the errors of the calls this server or client handles, on top of
    /// <see cref="DefaultPropagatedExceptionDataKeys"/>, at every level of the inner exception chain, landing in the caller's <see cref="RemoteException"/>.<see cref="Exception.Data"/>.
    /// Each value must be a string, opaque to CoreIpc; put a structured payload inside it (e.g. as JSON). <c>null</c> or empty (the default) sends nothing of its own.
    /// </summary>
    /// <remarks>
    /// The entries a <see cref="RemoteException"/> received travel on with it, listed or not, when it fails a call this server or client handles,
    /// whether rethrown as is or as an inner exception. A key listed here takes its current string value instead, or is dropped without one.
    /// A <see cref="RemoteException"/> keeps at most the first 64 entries it receives at each level.
    /// </remarks>
    public IReadOnlyCollection<string>? PropagatedExceptionDataKeys
    {
        get => _propagatedExceptionDataKeys;
        // Copied here, so a collection that can't be read fails where it's assigned, not on a connection.
        set => _propagatedExceptionDataKeys = value?.ToArray();
    }

    internal IReadOnlyCollection<string>? GetPropagatedExceptionDataKeys()
    {
        var keys = (DefaultPropagatedExceptionDataKeys ?? []).Union(PropagatedExceptionDataKeys ?? []).ToArray();
        return keys.Length > 0 ? keys : null;
    }
}
