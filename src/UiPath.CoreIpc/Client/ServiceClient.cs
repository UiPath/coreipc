using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using static System.Linq.Expressions.Expression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Runtime.ExceptionServices;

namespace UiPath.CoreIpc
{
    using ConnectionFactory = Func<Connection, CancellationToken, Task<Connection>>;
    using BeforeCallHandler = Func<CallInfo, CancellationToken, Task>;
    using InvokeAsyncDelegate = Func<IServiceClient, string, object[], object>;
    using RequestCompletionSource = TaskCompletionSource<Response>;

    public interface IServiceClient : IDisposable
    {
        Task<TResult> InvokeAsync<TResult>(string methodName, object[] args);
    }

    public class ServiceClient<TInterface> : IServiceClient where TInterface : class
    {
        private long _requestCounter = -1;
        private readonly ISerializer _serializer;
        private readonly TimeSpan _requestTimeout;
        protected readonly ILogger _logger;
        protected readonly ConnectionFactory _connectionFactory;
        protected readonly BeforeCallHandler _beforeCall;
        protected readonly ServiceEndpoint _serviceEndpoint;
        private readonly AsyncLock _connectionLock = new AsyncLock();
        private readonly ConcurrentDictionary<string, RequestCompletionSource> _requests = new ConcurrentDictionary<string, RequestCompletionSource>();
        protected Connection _connection;

        internal ServiceClient(ISerializer serializer, TimeSpan requestTimeout, ILogger logger, ConnectionFactory connectionFactory, BeforeCallHandler beforeCall = null, ServiceEndpoint serviceEndpoint = null)
        {
            _serializer = serializer;
            _requestTimeout = requestTimeout;
            _logger = logger;
            _connectionFactory = connectionFactory ?? ((_, __)=>Task.FromResult((Connection)null));
            _beforeCall = beforeCall ?? ((_, __) => Task.CompletedTask);
            _serviceEndpoint = serviceEndpoint;
        }

        public virtual string Name => _connection?.Name;

        private string NewRequestId() => Interlocked.Increment(ref _requestCounter).ToString();

        public TInterface CreateProxy()
        {
            var proxy = DispatchProxy.Create<TInterface, InterceptorProxy>();
            (proxy as InterceptorProxy).ServiceClient = this;
            return proxy;
        }

        protected Server OnNewConnection(Connection connection)
        {
            connection.ResponseReceived += OnResponseReceived;
            connection.Closed += OnConnectionClosed;
            return _serviceEndpoint == null ? null : new Server(_serviceEndpoint, connection);
        }

        private void OnConnectionClosed(object sender, EventArgs e)
        {
            foreach (var completionSource in _requests.Values)
            {
                completionSource.TrySetException(new IOException("Connection closed."));
            }
        }

        public async Task<TResult> InvokeAsync<TResult>(string methodName, object[] args)
        {
            byte[] requestBytes;
            TimeSpan timeout;
            var cancellationToken = args.OfType<CancellationToken>().LastOrDefault();
            var requestId = NewRequestId();
            Response response = null;
            return await Task.Run(async () =>
            {
                Serialize();

                await InvokeAsync();

                return _serializer.Deserialize<TResult>(response.CheckError().Data ?? "");
            }).ConfigureAwait(false);
            void Serialize()
            {
                var messageTimeout = args.OfType<Message>().FirstOrDefault()?.RequestTimeout.TotalSeconds;
                var request = new Request(requestId, methodName, args.Select(_serializer.Serialize).ToArray(), messageTimeout.GetValueOrDefault());
                requestBytes = _serializer.SerializeToBytes(request);
                timeout = request.GetTimeout(_requestTimeout);
            }
            Task InvokeAsync() =>
                cancellationToken.WithTimeout(timeout, async token =>
                {
                    bool newConnection;
                    using (await _connectionLock.LockAsync(token))
                    {
                        newConnection = await EnsureConnection(token);
                    }
                    await _beforeCall(new CallInfo(newConnection), token);
                    _logger?.LogInformation($"IpcClient calling {methodName} {requestId} {Name}.");
                    var requestCompletion = new RequestCompletionSource();
                    _requests[requestId] = requestCompletion;
                    try
                    {
                        await _connection.SendRequest(requestBytes, token);
                        response = await requestCompletion.Task.WaitAsync(token);
                    }
                    finally
                    {
                        _requests.TryRemove(requestId, out _);
                    }
                    _logger?.LogInformation($"IpcClient called {methodName} {requestId} {Name}.");
                }, methodName, ex =>
                {
                    var exception = ex;
                    if (cancellationToken.IsCancellationRequested && !(ex is TaskCanceledException))
                    {
                        exception = new TaskCanceledException(methodName, ex);
                    }
                    ExceptionDispatchInfo.Capture(exception).Throw();
                    return Task.CompletedTask;
                });
        }

        private void OnResponseReceived(object sender, DataReceivedEventsArgs responseReceivedEventsArgs)
        {
            var response = _serializer.Deserialize<Response>(responseReceivedEventsArgs.Data);
            _logger?.LogInformation($"Received response for request {response.RequestId} {Name}.");
            if (_requests.TryGetValue(response.RequestId, out var completionSource))
            {
                completionSource.TrySetResult(response);
            }
        }

        protected virtual Task<bool> ConnectToServerAsync(CancellationToken cancellationToken) => throw new NotImplementedException();

        protected async Task<bool> EnsureConnection(CancellationToken cancellationToken)
        {
            var externalConnection = await _connectionFactory(_connection, cancellationToken);
            if (externalConnection != null)
            {
                if (_connection == null)
                {
                    _connection = externalConnection;
                    OnNewConnection(_connection);
                    return true;
                }
                return false;
            }
            return await ConnectToServerAsync(cancellationToken);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            _logger?.LogInformation($"Dispose {Name}");
            if (disposing)
            {
                _connection?.Dispose();
            }
        }
    }

    public class InterceptorProxy : DispatchProxy, IDisposable
    {
        private static readonly MethodInfo InvokeAsyncMethod = typeof(IServiceClient).GetMethod(nameof(IServiceClient.InvokeAsync));
        private static readonly ConcurrentDictionary<Type, InvokeAsyncDelegate> _invokeAsyncByType = new ConcurrentDictionary<Type, InvokeAsyncDelegate>();

        public IServiceClient ServiceClient { get; internal set; }

        protected override object Invoke(MethodInfo targetMethod, object[] args) => GetInvokeAsync(targetMethod.ReturnType)(ServiceClient, targetMethod.Name, args);

        public void Dispose() => ServiceClient.Dispose();

        private static InvokeAsyncDelegate GetInvokeAsync(Type returnType) => _invokeAsyncByType.GetOrAdd(returnType, CreateDelegate);

        private static InvokeAsyncDelegate CreateDelegate(Type taskType)
        {
            var resultType = taskType.GetGenericArguments().SingleOrDefault() ?? typeof(object);
            var serviceClient = Parameter(typeof(IServiceClient), "serviceClient");
            var methodName = Parameter(typeof(string), "methodName");
            var methodArgs = Parameter(typeof(object[]), "methodArgs");
            var invokeAsyncMethod = InvokeAsyncMethod.MakeGenericMethod(resultType);
            var invokeAsyncCall = Call(serviceClient, invokeAsyncMethod, methodName, methodArgs);
            var lambda = Lambda<InvokeAsyncDelegate>(invokeAsyncCall, serviceClient, methodName, methodArgs);
            return lambda.Compile();
        }
    }
}