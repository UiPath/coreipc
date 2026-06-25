namespace UiPath.Ipc.Tests;

public interface ISystemService
{
    /// <summary>
    /// Returns the <paramref name="value"/> after the <paramref name="waitOnServer"/> is ellapsed.
    /// </summary>
    /// <param name="waitOnServer">The duration to wait before completing the operation.</param>
    /// <param name="ct">A <see cref="CancellationToken"/> to cancel the operation.</param>
    /// <returns>A task that completes successfully with a <c>null</c> result, after the specified <paramref name="waitOnServer"/>, or is canceled when the passed <see cref="CancellationToken"/> is signaled.</returns>
    Task<Guid> EchoGuidAfter(Guid value, TimeSpan waitOnServer, Message? message = null, CancellationToken ct = default);

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
    Task FireAndForget(TimeSpan wait);

    /// <summary>
    /// A method that does not return a result and whose algorithm will not be awaited by the remote client.
    /// </summary>
    /// <returns>A task that completes when the Ipc infrastructure confirms that the operation has begun but way before it has ended.</returns>
    Task FireAndForgetWithCt(CancellationToken ct);

    Task<string> EchoString(string value);

    Task<(string ExceptionType, string ExceptionMessage, string? MarshalledExceptionType)?> CallUnregisteredCallback(Message message = null!);

    Task<(string ExceptionType, string ExceptionMessage, string? MarshalledExceptionType)?> CallCallbackWithInexistentMethod(Message message = null!);

    Task FireAndForgetThrowSync();

    Task<string?> GetThreadName();

    Task<string> UploadEcho(Stream stream, CancellationToken ct = default);

    Task<bool> UploadJustCountBytes(Stream stream, int serverReadByteCount, TimeSpan serverDelay, CancellationToken ct = default);
    Task<Stream> Download(string s, CancellationToken ct = default);

    Task<int> AddIncrement(int x, int y, Message message = null!);

    Task<string> DanishNameOfDay(DayOfWeek day, CancellationToken ct);

    Task<byte[]> ReverseBytes(byte[] bytes, CancellationToken ct = default);
}

public interface IUnregisteredCallback
{
    Task<string> SomeMethod();
}

/// <summary>
/// An endpoint that no server registers — for testing that a REGULAR call
/// (not just a callback) to an unknown endpoint fails with
/// <see cref="EndpointNotFoundException"/>.
/// </summary>
public interface IInexistentEndpoint
{
    Task<Guid> Foo();
}

/// <summary>
/// Decoy contracts whose <see cref="Type.Name"/> collides with real,
/// registered contracts (routing keys on the simple name) but which declare
/// methods the real contracts lack — for testing
/// <see cref="MethodNotFoundException"/> on both directions.
/// </summary>
public static class Decoys
{
    public interface ISystemService
    {
        Task<Guid> InexistentMethod(CancellationToken ct = default);
    }

    public interface IArithmeticCallback
    {
        Task<int> IncrementInexistent(int x);
    }
}
