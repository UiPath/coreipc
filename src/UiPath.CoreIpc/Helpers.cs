using Microsoft.IO;
using System.Buffers;
using System.Collections.ObjectModel;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
namespace UiPath.CoreIpc;
using static CancellationTokenSourcePool;
public static class Helpers
{
    public const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
#if NET461
    public static CancellationTokenRegistration UnsafeRegister(this CancellationToken token, Action<object> callback, object state) => token.Register(callback, state);
    public static async Task ConnectAsync(this TcpClient tcpClient, IPAddress address, int port, CancellationToken cancellationToken)
    {
        using var token = cancellationToken.Register(state => ((TcpClient)state).Dispose(), tcpClient);
        await tcpClient.ConnectAsync(address, port);
    }
#endif
    public static Error ToError(this Exception ex) => new(ex.Message, ex.StackTrace ?? ex.GetBaseException().StackTrace, GetExceptionType(ex), ex.InnerException?.ToError());
    private static string GetExceptionType(Exception exception) => (exception as RemoteException)?.Type ?? exception.GetType().FullName;
    public static bool Enabled(this ILogger logger) => logger != null && logger.IsEnabled(LogLevel.Information);
    [Conditional("DEBUG")]
    public static void AssertDisposed(this SemaphoreSlim semaphore) =>
        Debug.Assert(typeof(SemaphoreSlim).GetField("m_waitHandle", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(semaphore) == null);
    public static TDelegate MakeGenericDelegate<TDelegate>(this MethodInfo genericMethod, Type genericArgument) where TDelegate : Delegate =>
        (TDelegate)genericMethod.MakeGenericMethod(genericArgument).CreateDelegate(typeof(TDelegate));
    public static MethodInfo GetStaticMethod(this Type type, string name) => type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
    public static MethodInfo GetInterfaceMethod(this Type type, string name)
    {
        var method = type.GetMethod(name, InstanceFlags) ?? 
            type.GetInterfaces().Select(t => t.GetMethod(name, InstanceFlags)).FirstOrDefault(m => m != null) ??
            throw new ArgumentOutOfRangeException(nameof(name), name, $"Method '{name}' not found in interface '{type}'.");
        if (method.IsGenericMethod)
        {
            throw new ArgumentOutOfRangeException(nameof(name), name, "Generic methods are not supported " + method);
        }
        return method;
    }
    public static IEnumerable<MethodInfo> GetInterfaceMethods(this Type type) =>
        type.GetMethods().Concat(type.GetInterfaces().SelectMany(i => i.GetMethods()));
    public static object GetDefaultValue(this ParameterInfo parameter) => parameter switch
    {
        { HasDefaultValue: false } => null,
        { ParameterType: { IsValueType: true }, DefaultValue: null } => Activator.CreateInstance(parameter.ParameterType),
        _ => parameter.DefaultValue
    };
    public static ReadOnlyDictionary<TKey, TValue> ToReadOnlyDictionary<TKey, TValue>(this IDictionary<TKey, TValue> dictionary) => new(dictionary);
    public static void LogException(this ILogger logger, Exception ex, object tag)
    {
        var message = $"{tag} # {ex}";
        if (logger != null)
        {
            logger.LogError(message);
        }
        else
        {
            Trace.TraceError(message);
        }
    }
    public static void LogException(this Task task, ILogger logger, object tag) => task.ContinueWith(result => logger.LogException(result.Exception, tag), TaskContinuationOptions.NotOnRanToCompletion);
}
public static class IOHelpers
{
    const int MaxBytes = 100 * 1024 * 1024;
    private static readonly RecyclableMemoryStreamManager Pool = new(MaxBytes, MaxBytes);
    internal static RecyclableMemoryStream GetStream(int size = 0) => (RecyclableMemoryStream)Pool.GetStream("IpcMessage", size);
    internal const int HeaderLength = sizeof(int) + 1;
    internal static NamedPipeServerStream NewNamedPipeServerStream(string pipeName, PipeDirection direction, int maxNumberOfServerInstances, PipeTransmissionMode transmissionMode, PipeOptions options, Func<PipeSecurity> pipeSecurity)
    {
#if NET461
        return new(pipeName, direction, maxNumberOfServerInstances, transmissionMode, options, inBufferSize: 0, outBufferSize: 0, pipeSecurity());
#elif WINDOWS
        return NamedPipeServerStreamAcl.Create(pipeName, direction, maxNumberOfServerInstances, transmissionMode, options, inBufferSize: 0, outBufferSize: 0, pipeSecurity());
#else
        return new(pipeName, direction, maxNumberOfServerInstances, transmissionMode, options);
#endif
    }

    public static PipeSecurity LocalOnly(this PipeSecurity pipeSecurity) => pipeSecurity.Deny(WellKnownSidType.NetworkSid, PipeAccessRights.FullControl);

    public static PipeSecurity Deny(this PipeSecurity pipeSecurity, WellKnownSidType sid, PipeAccessRights pipeAccessRights) =>
        pipeSecurity.Deny(new SecurityIdentifier(sid, null), pipeAccessRights);

    public static PipeSecurity Deny(this PipeSecurity pipeSecurity, IdentityReference sid, PipeAccessRights pipeAccessRights)
    {
        pipeSecurity.SetAccessRule(new(sid, pipeAccessRights, AccessControlType.Deny));
        return pipeSecurity;
    }

    public static PipeSecurity Allow(this PipeSecurity pipeSecurity, WellKnownSidType sid, PipeAccessRights pipeAccessRights) =>
        pipeSecurity.Allow(new SecurityIdentifier(sid, null), pipeAccessRights);

    public static PipeSecurity Allow(this PipeSecurity pipeSecurity, IdentityReference sid, PipeAccessRights pipeAccessRights)
    {
        pipeSecurity.SetAccessRule(new(sid, pipeAccessRights, AccessControlType.Allow));
        return pipeSecurity;
    }

    public static PipeSecurity AllowCurrentUser(this PipeSecurity pipeSecurity, bool onlyNonAdmin = false)
    {
        using (var currentIdentity = WindowsIdentity.GetCurrent())
        {
            if (onlyNonAdmin && new WindowsPrincipal(currentIdentity).IsInRole(WindowsBuiltInRole.Administrator))
            {
                return pipeSecurity;
            }
            pipeSecurity.Allow(currentIdentity.User, PipeAccessRights.ReadWrite|PipeAccessRights.CreateNewInstance);
        }
        return pipeSecurity;
    }

    public static bool PipeExists(string pipeName, int timeout = 1)
    {
        try
        {
            using (var client = new NamedPipeClientStream(pipeName))
            {
                client.Connect(timeout);
            }
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex.ToString());
        }
        return false;
    }

    internal static ValueTask WriteMessage(this Stream stream, MessageType messageType, RecyclableMemoryStream data, CancellationToken cancellationToken = default)
    {
        data.Position = 0;
        var buffer = data.GetSpan(HeaderLength);
        var totalLength = (int)data.Length;
        buffer[0] = (byte)messageType;
        var payloadLength = totalLength - HeaderLength;
        // https://github.com/dotnet/runtime/blob/85441ce69b81dfd5bf57b9d00ba525440b7bb25d/src/libraries/System.Private.CoreLib/src/System/BitConverter.cs#L133
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(buffer[1..]), payloadLength);
        return stream.WriteMessageCore(data, cancellationToken);
    }
#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private static async ValueTask WriteMessageCore(this Stream stream, RecyclableMemoryStream recyclableStream, CancellationToken cancellationToken)
    {
        using (recyclableStream)
        {
            await recyclableStream.CopyToAsync(stream, 0, cancellationToken);
        }
    }
    internal static Task WriteBuffer(this Stream stream, byte[] buffer, CancellationToken cancellationToken) => 
        stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
}
public static class Validator
{
    public static void Validate(ServiceHostBuilder serviceHostBuilder)
    {
        foreach (var endpointSettings in serviceHostBuilder.Endpoints.Values)
        {
            endpointSettings.Validate();
        }
    }

    public static void Validate<TDerived, TInterface>(ServiceClientBuilder<TDerived, TInterface> builder) where TInterface : class where TDerived : ServiceClientBuilder<TDerived, TInterface>
        => Validate(typeof(TInterface), builder.CallbackContract);

    public static void Validate(params Type[] contracts)
    {
        foreach (var contract in contracts.Where(c => c != null))
        {
            if (!contract.IsInterface)
            {
                throw new ArgumentOutOfRangeException(nameof(contract), "The contract must be an interface! " + contract);
            }
            foreach (var method in contract.GetInterfaceMethods())
            {
                Validate(method);
            }
        }
    }

    private static void Validate(MethodInfo method)
    {
        var returnType = method.ReturnType;
        CheckMethod();
        var parameters = method.GetParameters();
        for (int index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            CheckMessageParameter(index, parameter);
            CheckCancellationToken(index, parameter);
            if (parameter.ParameterType == typeof(Stream))
            {
                CheckStreamParameter(index);
            }
            else
            {
                CheckDerivedStream(method, parameter.ParameterType);
            }
        }
        void CheckStreamParameter(int index)
        {
            if (index != 0)
            {
                throw new ArgumentException($"The stream must be the first parameter! {method}");
            }
            if (!method.ReturnType.IsGenericType)
            {
                throw new ArgumentException($"Upload methods must return a value! {method}");
            }
        }
        void CheckMethod()
        {
            if (!typeof(Task).IsAssignableFrom(returnType))
            {
                throw new ArgumentException($"Method does not return Task! {method}");
            }
            if (returnType.IsGenericType)
            {
                var returnValueType = returnType.GenericTypeArguments[0];
                if (returnValueType != typeof(Stream))
                {
                    CheckDerivedStream(method, returnValueType);
                }
            }
        }
        void CheckMessageParameter(int index, ParameterInfo parameter)
        {
            if (typeof(Message).IsAssignableFrom(parameter.ParameterType) && index != 0)
            {
                throw new ArgumentException($"The message must be the first parameter! {method}");
            }
        }
        void CheckCancellationToken(int index, ParameterInfo parameter)
        {
            if (parameter.ParameterType == typeof(CancellationToken) && index != parameters.Length - 1)
            {
                throw new ArgumentException($"The CancellationToken parameter must be the last! {method}");
            }
        }
    }

    private static void CheckDerivedStream(MethodInfo method, Type type)
    {
        if (typeof(Stream).IsAssignableFrom(type))
        {
            throw new ArgumentException($"Stream parameters must be typed as Stream! {method}");
        }
    }
}
public readonly struct TimeoutHelper : IDisposable
{
    private static readonly Action<object> LinkedTokenCancelDelegate = static s => ((CancellationTokenSource)s).Cancel();
    private readonly PooledCancellationTokenSource _timeoutCancellationSource;
    private readonly CancellationToken _cancellationToken;
    private readonly CancellationTokenRegistration _linkedRegistration;
    public TimeoutHelper(TimeSpan timeout, CancellationToken token)
    {
        _timeoutCancellationSource = Rent();
        _timeoutCancellationSource.CancelAfter(timeout);
        _cancellationToken = token;
        _linkedRegistration = token.UnsafeRegister(LinkedTokenCancelDelegate, _timeoutCancellationSource);
    }
    public Exception CheckTimeout(Exception exception, string message)
    {
        if (_timeoutCancellationSource.IsCancellationRequested)
        {
            if (!_cancellationToken.IsCancellationRequested)
            {
                return new TimeoutException(message + " timed out.", exception);
            }
            if (exception is not TaskCanceledException)
            {
                return new TaskCanceledException(message, exception);
            }
        }
        return exception;
    }
    public void ThrowTimeout(Exception exception, string message)
    {
        var newException = CheckTimeout(exception, message);
        if (newException != exception)
        {
            throw newException;
        }
    }
    public void Dispose()
    {
        _linkedRegistration.Dispose();
        _timeoutCancellationSource.Return();
    }
    public CancellationToken Token => _timeoutCancellationSource.Token;
}