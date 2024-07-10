#if NET461

namespace System.Threading;

internal static class CancellationTokenExtensions
{
    public static CancellationTokenRegistration UnsafeRegister(this CancellationToken token, Action<object?> callback, object state)
    => token.Register(callback, state);
}

#endif
