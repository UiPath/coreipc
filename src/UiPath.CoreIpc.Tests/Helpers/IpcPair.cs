using UiPath.Ipc.Transport.NamedPipe;

namespace UiPath.Ipc.Tests;

internal sealed class IpcPair<TContract> : IAsyncDisposable where TContract : class
{
    private readonly IpcServer _server;
    public TContract Proxy { get; }

    private IpcPair(IpcServer server, TContract proxy)
    {
        _server = server;
        Proxy = proxy;
    }

    public static async Task<IpcPair<TContract>> Create(TContract service, Action<IpcServer>? configureServer = null, Action<IpcClient>? configureClient = null)
    {
        var pipeName = $"ipctest_pair_{Guid.NewGuid():N}";

        var server = new IpcServer
        {
            Transport = new NamedPipeServerTransport { PipeName = pipeName },
            Endpoints = new() { typeof(TContract) },
            ServiceProvider = new ServiceCollection()
                .AddLogging()
                .AddSingleton(service)
                .BuildServiceProvider(),
        };
        configureServer?.Invoke(server);

        var client = new IpcClient
        {
            Transport = new NamedPipeClientTransport { PipeName = pipeName },
        };
        configureClient?.Invoke(client);
        var proxy = client.GetProxy<TContract>();

        server.Start();
        await Task.Yield();
        return new IpcPair<TContract>(server, proxy);
    }

    public async ValueTask DisposeAsync()
    {
        (Proxy as IpcProxy)?.Dispose();
        await ((Proxy as IpcProxy)?.CloseConnection() ?? default);
        await _server.DisposeAsync();
    }
}
