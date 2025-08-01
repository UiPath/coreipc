﻿using Microsoft.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace UiPath.Ipc;

using static CancellationTokenSourcePool;

internal static class Helpers
{
    internal const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
    internal static Error ToError(this Exception ex) => new(ex.Message, ex.StackTrace ?? ex.GetBaseException().StackTrace!, GetExceptionType(ex), ex.InnerException?.ToError());
    private static string GetExceptionType(Exception exception) => (exception as RemoteException)?.Type ?? exception.GetType().FullName!;
    internal static bool Enabled(this ILogger? logger, LogLevel logLevel = LogLevel.Information) => logger is not null && logger.IsEnabled(logLevel);
    [Conditional("DEBUG")]
    internal static void AssertDisposed(this SemaphoreSlim semaphore) => semaphore.AssertFieldNull("m_waitHandle");
    [Conditional("DEBUG")]
    internal static void AssertDisposed(this CancellationTokenSource cts)
    {
#if NET461
        cts.AssertFieldNull("m_kernelEvent");
        cts.AssertFieldNull("m_timer");
#else
        cts.AssertFieldNull("_kernelEvent");
        cts.AssertFieldNull("_timer");
#endif
    }
    [Conditional("DEBUG")]
    static void AssertFieldNull(this object obj, string field) =>
        Debug.Assert(obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj) is null);
    internal static TDelegate MakeGenericDelegate<TDelegate>(this MethodInfo genericMethod, Type genericArgument) where TDelegate : Delegate =>
        (TDelegate)genericMethod.MakeGenericMethod(genericArgument).CreateDelegate(typeof(TDelegate));
    internal static MethodInfo GetInterfaceMethod(this Type type, string name)
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
    internal static IEnumerable<MethodInfo> GetInterfaceMethods(this Type type) =>
        type.GetMethods().Concat(type.GetInterfaces().SelectMany(i => i.GetMethods()));
    internal static object? GetDefaultValue(this ParameterInfo parameter) => parameter switch
    {
        { HasDefaultValue: false } => null,
        { ParameterType: { IsValueType: true }, DefaultValue: null } => Activator.CreateInstance(parameter.ParameterType),
        _ => parameter.DefaultValue
    };
    internal static ReadOnlyDictionary<TKey, TValue> ToReadOnlyDictionary<TKey, TValue>(this IDictionary<TKey, TValue> dictionary) where TKey : notnull => new(dictionary);
    internal static void LogException(this ILogger? logger, Exception ex, object tag)
    {
        var message = $"{tag} # {ex}";

        if (logger is not null)
        {
            logger.LogError(ex, message);
            return;
        }

        Trace.TraceError(message);
    }

    internal static void TraceError(this Task task)
    {
        task.ContinueWith(task =>
        {
            Trace.TraceError(task.Exception!.ToString());
        }, TaskContinuationOptions.NotOnRanToCompletion);
    }

    internal static void LogException(this Task task, ILogger? logger, object tag) => task.ContinueWith(result => logger.LogException(result.Exception!, tag), TaskContinuationOptions.NotOnRanToCompletion);

    internal static void WaitAndUnwrapException(this Task task)
    {
        if (task is null)
        {
            throw new ArgumentNullException(nameof(task));
        }

        task.GetAwaiter().GetResult();
    }
}
public static class IOHelpers
{
    const int MaxBytes = 100 * 1024 * 1024;
    private static readonly RecyclableMemoryStreamManager Pool = new(MaxBytes, MaxBytes);
    internal static MemoryStream GetStream(int size = 0) => Pool.GetStream("IpcMessage", size);
    internal const int HeaderLength = sizeof(int) + 1;
    internal static NamedPipeServerStream NewNamedPipeServerStream(string pipeName, PipeDirection direction, int maxNumberOfServerInstances, PipeTransmissionMode transmissionMode, PipeOptions options, Func<PipeSecurity?> pipeSecurity)
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
        using (var currentIdentity = WindowsIdentity.GetCurrent()!)
        {
            if (onlyNonAdmin && new WindowsPrincipal(currentIdentity).IsInRole(WindowsBuiltInRole.Administrator))
            {
                return pipeSecurity;
            }

            pipeSecurity.Allow(currentIdentity.User!, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance);
        }
        return pipeSecurity;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static bool PipeExists(string pipeName, int timeoutMilliseconds = 1) => PipeHelper.PipeExists(pipeName, timeoutMilliseconds);

    internal static ValueTask WriteMessage(this Stream stream, MessageType messageType, Stream data, CancellationToken cancellationToken = default)
    {
        var recyclableStream = (RecyclableMemoryStream)data;
        recyclableStream.Position = 0;
        var buffer = recyclableStream.GetSpan(HeaderLength);
        var totalLength = (int)recyclableStream.Length;
        buffer[0] = (byte)messageType;
        var payloadLength = totalLength - HeaderLength;
        // https://github.com/dotnet/runtime/blob/85441ce69b81dfd5bf57b9d00ba525440b7bb25d/src/libraries/System.Private.CoreLib/src/System/BitConverter.cs#L133
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(buffer.Slice(1)), payloadLength);
        return stream.WriteMessageCore(recyclableStream, cancellationToken);
    }
#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private static async ValueTask WriteMessageCore(this Stream stream, RecyclableMemoryStream recyclableStream, CancellationToken cancellationToken)
    {
        using (recyclableStream)
        {
            try
            {
                await recyclableStream.CopyToAsync(stream, 0, cancellationToken);
            }
            catch
            {
                throw;
            }
        }
    }
    internal static Task WriteBuffer(this Stream stream, byte[] buffer, CancellationToken cancellationToken) =>
        stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);

    private static readonly IPipeHelper PipeHelper = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? new PipeUtilsWindows() : new PipeUtilsPortable();

    private interface IPipeHelper
    {
        bool PipeExists(string pipeName, int timeoutMilliseconds);
    }

    private sealed class PipeUtilsWindows : IPipeHelper
    {
        public bool PipeExists(string pipeName, int timeoutMilliseconds) => WaitNamedPipe($@"\\.\pipe\{pipeName}", timeoutMilliseconds);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern bool WaitNamedPipe(string pipeName, int timeoutMilliseconds);
    }

    private sealed class PipeUtilsPortable : IPipeHelper
    {
        public bool PipeExists(string pipeName, int timeoutMilliseconds)
        {
            try
            {
                using (var client = new NamedPipeClientStream(pipeName))
                {
                    client.Connect(timeoutMilliseconds);
                }
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex.ToString());
            }
            return false;
        }
    }
}
internal static class Validator
{
    public static void Validate(params Type[] contracts)
    => Validate(contracts.AsEnumerable());

    public static void Validate(IEnumerable<Type> contracts)
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
        int streamCount = 0;
        for (int index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            CheckMessageParameter(index, parameter);
            CheckCancellationToken(index, parameter);
            if (parameter.ParameterType == typeof(Stream))
            {
                CheckStreamParameter();
            }
            else
            {
                CheckDerivedStream(method, parameter.ParameterType);
            }
        }
        void CheckStreamParameter()
        {
            streamCount++;
            if (streamCount > 1)
            {
                throw new ArgumentException($"Only one Stream parameter is allowed! {method}");
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
            if (typeof(Message).IsAssignableFrom(parameter.ParameterType) && index != parameters.Length - 1 &&
                parameters[parameters.Length - 1].ParameterType != typeof(CancellationToken))
            {
                throw new ArgumentException($"The message must be the last parameter before the cancellation token! {method}");
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
internal readonly struct TimeoutHelper : IDisposable
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
        _linkedRegistration = token.UnsafeRegister(LinkedTokenCancelDelegate!, _timeoutCancellationSource);
    }

    public static string ComputeTimeoutMessage(string operation) => $"{operation} timed out.";

    public Exception CheckTimeout(Exception exception, string message)
    {
        if (_timeoutCancellationSource.IsCancellationRequested)
        {
            if (!_cancellationToken.IsCancellationRequested)
            {
                return new TimeoutException(ComputeTimeoutMessage(message), exception);
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