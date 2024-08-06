namespace UiPath.Ipc.Tests;

public interface ISystemService
{
    /// <summary>
    /// Returns the <paramref name="value"/> after the <paramref name="duration"/> is ellapsed.
    /// </summary>
    /// <param name="duration">The duration to wait before completing the operation.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> to cancel the operation.</param>
    /// <returns>A task that completes successfully with a <c>null</c> result, after the specified <paramref name="duration"/>, or is canceled when the passed <see cref="CancellationToken"/> is signaled.</returns>
    Task<Guid> EchoGuidAfter(Guid value, TimeSpan duration, CancellationToken ct = default);

    /// <summary>
    /// Returns <c>true</c> if the received <see cref="Message"/> is not <c>null</c>.
    /// </summary>
    /// <param name="message">An optional <see cref="Message"/>.</param>
    /// <returns></returns>
    Task<bool> MessageReceivedAsNotNull(Message? message = null);

    /// <summary>
    /// A method that does not return a result and whose algorithm will not be awaited by the remote client.
    /// </summary>
    /// <returns>A task that completes when the Ipc infrastructure confirms that the operation has begun but way before it has ended.</returns>
    Task FireAndForget();

    Task<string> EchoString(string value);

    Task<(string ExceptionType, string ExceptionMessage, string? MarshalledExceptionType)?> CallUnregisteredCallback(Message message = null!);

    Task FireAndForgetThrowSync();

    Task<string?> GetThreadName();

    Task<string> UploadEcho(Stream stream, bool trace, CancellationToken ct = default);

    Task<bool> UploadJustCountBytes(Stream stream, int serverReadByteCount, TimeSpan serverDelay, CancellationToken ct = default);
    Task<Stream> Download(string s, CancellationToken ct = default);
}

public interface IUnregisteredCallback
{
    Task<string> SomeMethod();
}
