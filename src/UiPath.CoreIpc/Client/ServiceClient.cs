namespace UiPath.Ipc;

internal abstract class ServiceClient : IDisposable
{
    #region " NonGeneric-Generic adapter cache "
    private static readonly MethodInfo GenericDefinition = ((Func<ServiceClient, MethodInfo, object?[], Task<int>>)Invoke<int>).Method.GetGenericMethodDefinition();
    private static readonly ConcurrentDictionary<Type, InvokeDelegate> ReturnTypeToInvokeDelegate = new();
    private static InvokeDelegate GetInvokeDelegate(Type returnType) => ReturnTypeToInvokeDelegate.GetOrAdd(returnType, CreateInvokeDelegate);
    private static InvokeDelegate CreateInvokeDelegate(Type returnType)
    => GenericDefinition.MakeGenericDelegate<InvokeDelegate>(
        returnType.IsGenericType
            ? returnType.GetGenericArguments()[0]
            : typeof(object));

    private static Task<TResult> Invoke<TResult>(ServiceClient serviceClient, MethodInfo method, object?[] args) => serviceClient.Invoke<TResult>(method, args);
    #endregion

    public abstract void Dispose();
    public virtual ValueTask CloseConnection() => throw new NotSupportedException();

    public object? Invoke(MethodInfo method, object?[] args) => GetInvokeDelegate(method.ReturnType)(this, method, args);
    protected abstract Task<TResult> Invoke<TResult>(MethodInfo method, object?[] args);
}

internal abstract class ServiceClient<TInterface> : ServiceClient where TInterface : class
{
    internal Server? _server;

    protected abstract TimeSpan RequestTimeout { get; }
    protected abstract ConnectionFactory? ConnectionFactory { get; }
    protected abstract BeforeCallHandler? BeforeCall { get; }
    protected abstract ILogger? Log { get; }
    protected abstract string DebugName { get; }
    protected abstract ISerializer? Serializer { get; }
    protected abstract Task<(Connection connection, bool newlyConnected)> EnsureConnection(CancellationToken ct);

    public TInterface CreateProxy()
    {
        var proxy = DispatchProxy.Create<TInterface, IpcProxy>();
        (proxy as IpcProxy)!.ServiceClient = this;
        return proxy;
    }

    protected override Task<TResult> Invoke<TResult>(MethodInfo method, object?[] args)
    {
        var syncContext = SynchronizationContext.Current;
        var defaultContext = syncContext is null || syncContext.GetType() == typeof(SynchronizationContext);
        return defaultContext ? Invoke() : Task.Run(Invoke);

        async Task<TResult> Invoke()
        {
            CancellationToken cancellationToken = default;
            TimeSpan messageTimeout = default;
            TimeSpan clientTimeout = RequestTimeout;
            Stream? uploadStream = null;
            var methodName = method.Name;

            var serializedArguments = SerializeArguments();

            var timeoutHelper = new TimeoutHelper(clientTimeout, cancellationToken);
            try
            {
                var ct = timeoutHelper.Token;

                var (connection, newConnection) = await EnsureConnection(ct);

                if (BeforeCall is not null)
                {
                    var callInfo = new CallInfo(newConnection, method, args);
                    await BeforeCall(callInfo, ct);
                }

                var requestId = connection.NewRequestId();
                var request = new Request(typeof(TInterface).Name, requestId, methodName, serializedArguments, messageTimeout.TotalSeconds)
                {
                    UploadStream = uploadStream
                };

                Log?.ServiceClientCalling(methodName, requestId, DebugName);
                var response = await connection.RemoteCall(request, ct); // returns user errors instead of throwing them (could throw for system bugs)
                Log?.ServiceClientCalled(methodName, requestId, DebugName);

                return response.Deserialize<TResult>(Serializer);
            }
            catch (Exception ex)
            {
                timeoutHelper.ThrowTimeout(ex, methodName);
                throw;
            }
            finally
            {
                timeoutHelper.Dispose();
            }

            string[] SerializeArguments()
            {
                var result = new string[args.Length];

                for (int index = 0; index < args.Length; index++)
                {
                    switch (args[index])
                    {
                        case Message { RequestTimeout: var requestTimeout } when requestTimeout != TimeSpan.Zero:
                            messageTimeout = requestTimeout;
                            clientTimeout = requestTimeout;
                            break;
                        case CancellationToken token:
                            cancellationToken = token;
                            args[index] = "";
                            break;
                        case Stream stream:
                            uploadStream = stream;
                            args[index] = "";
                            break;
                    }

                    result[index] = Serializer.OrDefault().Serialize(args[index]);
                }

                return result;
            }
        }
    }

    public sealed override void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        Log?.ServiceClientDispose(DebugName);
    }

    public override string ToString() => DebugName;
}

internal sealed class ServiceClientProper<TInterface> : ServiceClient<TInterface> where TInterface : class
{
    private readonly FastAsyncLock _lock = new();
    private readonly ClientBase _client;

    private Connection? _latestConnection;

    public ServiceClientProper(ClientBase client)
    {
        _client = client;
    }

    public override async ValueTask CloseConnection()
    {
        using (await _lock.Lock())
        {
            _latestConnection?.Dispose();
            _latestConnection = null;
        }
    }

    protected override async Task<(Connection connection, bool newlyConnected)> EnsureConnection(CancellationToken ct)
    {
        using (await _lock.Lock(ct))
        {
            if (_latestConnection is not null && _client.IsConnected(_latestConnection.Network))
            {
                return (_latestConnection, newlyConnected: false);
            }

            _latestConnection = new Connection(await Connect(ct), Serializer, Log, DebugName);
            return (_latestConnection, newlyConnected: true);
        }
    }
    private async Task<Network> Connect(CancellationToken ct)
    {
        if (ConnectionFactory is not null 
            && await ConnectionFactory(ct) is { } userProvidedNetwork)
        {
            return userProvidedNetwork;
        }

        return await _client.Connect(ct);
    }

    protected override TimeSpan RequestTimeout => _client.RequestTimeout;
    protected override ConnectionFactory? ConnectionFactory => _client.ConnectionFactory;
    protected override BeforeCallHandler? BeforeCall => _client.BeforeCall;
    protected override ILogger? Log => _client.Logger;
    protected override string DebugName => "Some ServiceClient"; // TODO: get the DebugName from the client
    protected override ISerializer? Serializer => _client.Serializer;
}

internal sealed class ServiceClientForCallback<TInterface> : ServiceClient<TInterface> where TInterface : class
{
    private readonly Connection _connection;
    private readonly Listener _listener;

    public ServiceClientForCallback(Connection connection, Listener listener)
    {
        _connection = connection;
        _listener = listener;
    }

    protected override Task<(Connection connection, bool newlyConnected)> EnsureConnection(CancellationToken ct)
    => Task.FromResult((_connection, newlyConnected: false));

    protected override TimeSpan RequestTimeout => throw new NotImplementedException();
    protected override ConnectionFactory? ConnectionFactory => null;
    protected override BeforeCallHandler? BeforeCall => null;
    protected override ILogger? Log => null;
    protected override string DebugName => throw new NotImplementedException();
    protected override ISerializer? Serializer => null;
}

public class IpcProxy : DispatchProxy, IDisposable
{
    internal ServiceClient ServiceClient { get; set; } = null!;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    => ServiceClient.Invoke(targetMethod!, args!);

    public void Dispose() => ServiceClient?.Dispose();

    public ValueTask CloseConnection() => ServiceClient.CloseConnection();
}
