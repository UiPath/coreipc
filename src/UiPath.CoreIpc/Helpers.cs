using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc
{
    public static class Helpers
    {
        public const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
        public static MethodInfo GetInterfaceMethod(this Type type, string name) => type.GetMethod(name, InstanceFlags) ??
            type.GetInterfaces().Select(t => t.GetMethod(name, InstanceFlags)).FirstOrDefault(m => m != null);
        public static IEnumerable<MethodInfo> GetInterfaceMethods(this Type type) =>
            type.GetMethods().Concat(type.GetInterfaces().SelectMany(i => i.GetMethods()));
        public static object GetDefaultValue(this ParameterInfo parameter) => parameter switch
        {
            { HasDefaultValue: false } => throw new ArgumentException($"{parameter} has no default value!"),
            { ParameterType: { IsValueType: true }, DefaultValue: null } => Activator.CreateInstance(parameter.ParameterType),
            _ => parameter.DefaultValue
        };
        public static ReadOnlyDictionary<TKey, TValue> ToReadOnlyDictionary<TKey, TValue>(this IDictionary<TKey, TValue> dictionary) => new(dictionary);
        public static async Task<bool> WithResult(this Task task)
        {
            await task;
            return true;
        }
        public static Task<TResult> WithTimeout<TResult>(this CancellationToken cancellationToken,
            TimeSpan timeout, Func<CancellationToken, Task<TResult>> func, string message, Func<Exception, Task> exceptionHandler) =>
            new[] { cancellationToken }.WithTimeout(timeout, func, message, exceptionHandler);
        public static Task WithTimeout(this IEnumerable<CancellationToken> cancellationTokens,
            TimeSpan timeout, Func<CancellationToken, Task> func, string message, Func<Exception, Task> exceptionHandler) =>
            cancellationTokens.WithTimeout(timeout, token => func(token).WithResult(), message, exceptionHandler);
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
        public static async Task<TResult> WithTimeout<TResult>(this IEnumerable<CancellationToken> cancellationTokens,
            TimeSpan timeout, Func<CancellationToken, Task<TResult>> func, string message, Func<Exception, Task> exceptionHandler)
        {
            var timeoutCancellationSource = new CancellationTokenSource(timeout);
            var linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokens.Concat(new[] { timeoutCancellationSource.Token }).ToArray());
            try
            {
                return await func(linkedCancellationSource.Token);
            }
            catch (Exception ex)
            {
                var exception = ex;
                if (timeoutCancellationSource.IsCancellationRequested)
                {
                    exception = new TimeoutException(message + " timed out.", ex);
                }
                Dispose();
                await exceptionHandler(exception);
            }
            finally
            {
                Dispose();
            }
            return default;
            void Dispose()
            {
                timeoutCancellationSource.Dispose();
                linkedCancellationSource.Dispose();
            }
        }
    }
    public static class IOHelpers
    {
        internal static NamedPipeServerStream NewNamedPipeServerStream(string pipeName, PipeDirection direction, int maxNumberOfServerInstances, PipeTransmissionMode transmissionMode, PipeOptions options, Func<PipeSecurity> pipeSecurity)
        {
#if NET461
            return new(pipeName, direction, maxNumberOfServerInstances, transmissionMode, options, inBufferSize: 0, outBufferSize: 0, pipeSecurity());
#elif NET5_0_WINDOWS
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

        internal static async Task WriteMessage(this Stream stream, WireMessage message, CancellationToken cancellationToken = default)
        {
            await stream.WriteBuffer(new[] { (byte)message.MessageType }, cancellationToken);
            await stream.WriteBuffer(BitConverter.GetBytes(message.Data.Length), cancellationToken);
            await stream.WriteBuffer(message.Data, cancellationToken);
        }

        internal static Task WriteBuffer(this Stream stream, byte[] buffer, CancellationToken cancellationToken) => 
            stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);

        internal static async Task<WireMessage> ReadMessage(this Stream stream, int maxMessageSize, CancellationToken cancellationToken = default)
        {
            var messageTypeBuffer = await stream.ReadBufferCore(1, cancellationToken);
            if (messageTypeBuffer.Length == 0)
            {
                return new(default, messageTypeBuffer);
            }
            var messageType = (MessageType)messageTypeBuffer[0];
            var lengthBuffer = await stream.ReadBufferCore(sizeof(int), cancellationToken);
            if (lengthBuffer.Length == 0)
            {
                return new(messageType, lengthBuffer);
            }
            var length = BitConverter.ToInt32(lengthBuffer, 0);
            if(length > maxMessageSize)
            {
                throw new InvalidDataException($"Message too large. The maximum message size is {maxMessageSize/(1024*1024)} megabytes.");
            }
            var messageData = await stream.ReadBuffer(length, cancellationToken);
            return new(messageType, messageData);
        }

        internal static async Task<byte[]> ReadBuffer(this Stream stream, int length, CancellationToken cancellationToken)
        {
            var messageData = await stream.ReadBufferCore(length, cancellationToken);
            if (messageData.Length == 0)
            {
                throw new IOException("Connection closed.");
            }
            return messageData;
        }

        private static async Task<byte[]> ReadBufferCore(this Stream stream, int length, CancellationToken cancellationToken)
        {
            var bytes = new byte[length];
            int offset = 0;
            int remaining = length;
            while(remaining > 0)
            {
                var read = await stream.ReadAsync(bytes, offset, remaining, cancellationToken);
                if(read == 0)
                {
                    return Array.Empty<byte>();
                }
                remaining -= read;
                offset += read;
            }
            return bytes;
        }

        public static byte[] SerializeToBytes(this ISerializer serializer, object obj)
        {
            var json = serializer.Serialize(obj);
            return Encoding.UTF8.GetBytes(json);
        }

        public static T Deserialize<T>(this ISerializer serializer, byte[] binary) => serializer.Deserialize<T>(Encoding.UTF8.GetString(binary));

        public static T Deserialize<T>(this ISerializer serializer, string json) => (T)serializer.Deserialize(json, typeof(T));
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
    public readonly struct ConcurrentDictionaryWrapper<TKey, TValue>
    {
        private readonly ConcurrentDictionary<TKey, TValue> _dictionary;
        private readonly Func<TKey, TValue> _valueFactory;
        public ConcurrentDictionaryWrapper(Func<TKey, TValue> valueFactory, int capacity = 31)
        {
            _dictionary = new(Environment.ProcessorCount, capacity);
            _valueFactory = key => valueFactory(key);
        }
        public TValue GetOrAdd(in TKey key) => _dictionary.GetOrAdd(key, _valueFactory);
        public bool TryGetValue(TKey key, out TValue value) => _dictionary.TryGetValue(key, out value);
        public bool TryRemove(TKey key, out TValue value) => _dictionary.TryRemove(key, out value);
    }
}