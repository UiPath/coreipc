namespace UiPath.Ipc.Tests;

internal static class Constants
{
    /// <summary>
    /// When signalling a <see cref="CancellationToken"/>, before the server receives and honours the <see cref="CancellationRequest"/>, the local proxy will have thrown <see cref="OperationCanceledException"/>.
    /// This value represents an exagerated timeout beyond which it's clear that a bug has occurred, even when running on a CI agent under load.
    /// </summary>
    public static readonly TimeSpan Timeout_LocalProxyToThrowOCE = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Considering a service method which returns instantly, and the time it takes for UiPath.Ipc to complete a full roundtrip from calling to receiving the result,
    /// this value represents an exagerated timeout beyond which it's clear that a bug has occurred, even when running on a CI agent under load.
    /// </summary>
    public static readonly TimeSpan Timeout_IpcRoundtrip = TimeSpan.FromMilliseconds(600);

    public static readonly TimeSpan Timeout_Short = TimeSpan.FromMilliseconds(100);
}
