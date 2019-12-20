using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
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
    public static class IOHelpers
    {
        public static ReadOnlyDictionary<TKey, TValue> ToReadOnlyDictionary<TKey, TValue>(this IDictionary<TKey, TValue> dictionary) => new ReadOnlyDictionary<TKey, TValue>(dictionary);

        [Conditional("DEBUG")]
        public static void Validate(Type contract)
        {
            if (!contract.IsInterface)
            {
                throw new ArgumentOutOfRangeException(nameof(contract), "The contract must be an interface! " + contract);
            }
            foreach (var method in contract.GetAllMethods())
            {
                Validate(method);
            }
        }

        private static void Validate(MethodInfo method)
        {
            if (!typeof(Task).IsAssignableFrom(method.ReturnType))
            {
                throw new ArgumentException($"Method does not return Task! {method}");
            }
            var parameters = method.GetParameters();
            for (int index = 0; index < parameters.Length; index++)
            {
                var parameter = parameters[index];
                if (typeof(Message).IsAssignableFrom(parameter.ParameterType) && index != parameters.Length - 1 && parameters[parameters.Length - 1].ParameterType != typeof(CancellationToken))
                {
                    throw new ArgumentException($"The message must be the last parameter before the cancellation token! {method}");
                }
                if (parameter.ParameterType == typeof(CancellationToken) && index != parameters.Length - 1)
                {
                    throw new ArgumentException($"The CancellationToken parameter must be the last! {method}");
                }
            }
        }

        public static object GetDefaultValue(this ParameterInfo parameter)
        {
            if (!parameter.HasDefaultValue)
            {
                throw new ArgumentException($"{parameter} has no default value!");
            }
            if (parameter.DefaultValue == null && parameter.ParameterType.IsValueType)
            {
                return Activator.CreateInstance(parameter.ParameterType);
            }
            return parameter.DefaultValue;
        }

        public static PipeSecurity LocalOnly(this PipeSecurity pipeSecurity) => pipeSecurity.Deny(WellKnownSidType.NetworkSid, PipeAccessRights.FullControl);

        public static PipeSecurity Deny(this PipeSecurity pipeSecurity, WellKnownSidType sid, PipeAccessRights pipeAccessRights) =>
            pipeSecurity.Deny(new SecurityIdentifier(sid, null), pipeAccessRights);

        public static PipeSecurity Deny(this PipeSecurity pipeSecurity, IdentityReference sid, PipeAccessRights pipeAccessRights)
        {
            pipeSecurity.SetAccessRule(new PipeAccessRule(sid, pipeAccessRights, AccessControlType.Deny));
            return pipeSecurity;
        }

        public static PipeSecurity Allow(this PipeSecurity pipeSecurity, WellKnownSidType sid, PipeAccessRights pipeAccessRights) =>
            pipeSecurity.Allow(new SecurityIdentifier(sid, null), pipeAccessRights);

        public static PipeSecurity Allow(this PipeSecurity pipeSecurity, IdentityReference sid, PipeAccessRights pipeAccessRights)
        {
            pipeSecurity.SetAccessRule(new PipeAccessRule(sid, pipeAccessRights, AccessControlType.Allow));
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

        public static async Task<bool> WithResult(this Task task)
        {
            await task;
            return true;
        }

        public static Task WithTimeout(this CancellationToken cancellationToken,
            TimeSpan timeout, Func<CancellationToken, Task> func, string message, Func<Exception, Task> exceptionHandler) =>
            new[] { cancellationToken }.WithTimeout(timeout, func, message, exceptionHandler);

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
            using(var timeoutCancellationSource = new CancellationTokenSource(timeout))
            using(var linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokens.Concat(new[] { timeoutCancellationSource.Token }).ToArray()))
            {
                try
                {
                    return await func(linkedCancellationSource.Token);
                }
                catch(Exception ex) 
                {
                    var exception = ex;
                    if(timeoutCancellationSource.IsCancellationRequested)
                    {
                        exception = new TimeoutException(message + " timed out.", ex);
                    }
                    timeoutCancellationSource.Dispose();
                    linkedCancellationSource.Dispose();
                    await exceptionHandler(exception);
                }
                return default;
            }
        }

        public static bool PipeExists(string pipeName)
        {
            Thread.Sleep(1); // checking pipe consumes a connection; don't check too frequently
            return File.Exists(@"\\.\pipe\" + pipeName);
        }

        internal static async Task WriteMessage(this Stream stream, WireMessage message, CancellationToken cancellationToken = default)
        {
            await stream.WriteAsync(new[] { (byte)message.MessageType }, 0, 1, cancellationToken);
            var lengthBuffer = BitConverter.GetBytes(message.Data.Length);
            await stream.WriteBuffer(lengthBuffer, cancellationToken);
            await stream.WriteBuffer(message.Data, cancellationToken);
        }

        private static Task WriteBuffer(this Stream stream, byte[] buffer, CancellationToken cancellationToken) => 
            stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);

        internal static async Task<WireMessage> ReadMessage(this Stream stream, int maxMessageSize = int.MaxValue, CancellationToken cancellationToken = default)
        {
            var messageTypeBuffer = await stream.ReadBuffer(1, cancellationToken);
            if (messageTypeBuffer.Length == 0)
            {
                return new WireMessage(default, messageTypeBuffer);
            }
            var messageType = (MessageType)messageTypeBuffer[0];
            var lengthBuffer = await stream.ReadBuffer(sizeof(int), cancellationToken);
            if (lengthBuffer.Length == 0)
            {
                return new WireMessage(messageType, lengthBuffer);
            }
            var length = BitConverter.ToInt32(lengthBuffer, 0);
            if(length > maxMessageSize)
            {
                throw new InvalidDataException($"Message too large. The maximum message size is {maxMessageSize/(1024*1024)} megabytes.");
            }
            var messageData = await stream.ReadBuffer(length, cancellationToken);
            if (messageData.Length == 0)
            {
                throw new IOException("Connection closed.");
            }
            return new WireMessage(messageType, messageData);
        }

        private static async Task<byte[]> ReadBuffer(this Stream stream, int length, CancellationToken cancellationToken)
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

        public static MethodInfo GetInheritedMethod(this Type type, string name) => type.GetInheritedMember(name) as MethodInfo;

        public static MemberInfo GetInheritedMember(this Type type, string name) => type.GetAllMembers().SingleOrDefault(mi => mi.Name == name);

        public static IEnumerable<MethodInfo> GetAllMethods(this Type type) => type.GetAllMembers().OfType<MethodInfo>();

        private static IEnumerable<MemberInfo> GetAllMembers(this Type type) =>
            type.GetTypeInheritance().Concat(type.GetTypeInfo().ImplementedInterfaces).SelectMany(i => i.GetMembers());

        public static IEnumerable<Type> GetTypeInheritance(this Type type)
        {
            yield return type;

            var baseType = type.BaseType;
            while (baseType != null)
            {
                yield return baseType;
                baseType = baseType.BaseType;
            }
        }
    }
}