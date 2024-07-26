using UiPath.Ipc.BackCompat;
using UiPath.Ipc.Transport.NamedPipe;

namespace UiPath.Ipc.Tests;

public class EndpointTests : IAsyncDisposable
{
    private static TimeSpan RequestTimeout => TestBase.RequestTimeout;
    private readonly ServiceHost _host;
    private readonly IComputingService _computingClient;
    private readonly ISystemService _systemClient;
    private readonly ComputingService _computingService;
    private readonly SystemService _systemService;
    private readonly ComputingCallback _computingCallback;
    private readonly SystemCallback _systemCallback;
    private readonly IServiceProvider _serviceProvider;

    public EndpointTests()
    {
        _computingCallback = new ComputingCallback { Id = Guid.NewGuid().ToString() };
        _systemCallback = new SystemCallback { Id = Guid.NewGuid().ToString() };
        _serviceProvider = IpcHelpers.ConfigureServices();
        _computingService = (ComputingService)_serviceProvider.GetService<IComputingService>();
        _systemService = (SystemService)_serviceProvider.GetService<ISystemService>();
        _host = new ServiceHostBuilder(_serviceProvider)
            .UseNamedPipes(new NamedPipeListener()
            {
                PipeName = PipeName,
                RequestTimeout = RequestTimeout
            })
            .AddEndpoint<IComputingServiceBase>()
            .AddEndpoint<IComputingService>()
            .AddEndpoint<ISystemService>()
            .ValidateAndBuild();
        _host.RunAsync();
        _computingClient = ComputingClientBuilder().ValidateAndBuild();
        _systemClient = CreateSystemService();
    }
    public string PipeName => nameof(EndpointTests) + GetHashCode();

    private NamedPipeClientBuilder<IComputingService, IComputingCallback> ComputingClientBuilder(TaskScheduler taskScheduler = null)
    => new NamedPipeClientBuilder<IComputingService, IComputingCallback>(PipeName, _serviceProvider)
        .AllowImpersonation()
        .RequestTimeout(RequestTimeout)
        .CallbackInstance(_computingCallback)
        .TaskScheduler(taskScheduler);

    private ISystemService CreateSystemService() => SystemClientBuilder().ValidateAndBuild();

    private NamedPipeClientBuilder<ISystemService, ISystemCallback> SystemClientBuilder()
    => new NamedPipeClientBuilder<ISystemService, ISystemCallback>(PipeName, _serviceProvider)
        .CallbackInstance(_systemCallback)
        .RequestTimeout(RequestTimeout)
        .AllowImpersonation();

    public async ValueTask DisposeAsync()
    {
        ((IDisposable)_computingClient).Dispose();
        ((IDisposable)_systemClient).Dispose();
        await ((IpcProxy)_computingClient).CloseConnection();
        await ((IpcProxy)_systemClient).CloseConnection();
        await _host.DisposeAsync();
    }
    [Fact]
    public Task CallbackConcurrently() => Task.WhenAll(Enumerable.Range(1, 50).Select(_ => CallbackCore()));
    [Fact]
    public async Task Callback()
    {
        for (int index = 0; index < 50; index++)
        {
            await CallbackCore();
            await ((IpcProxy)_computingClient).CloseConnection();
        }
    }

    private async Task CallbackCore()
    {
        var proxy = new NamedPipeClientBuilder<IComputingServiceBase>(PipeName)
            .RequestTimeout(RequestTimeout).AllowImpersonation().ValidateAndBuild();
        var message = new SystemMessage { Text = Guid.NewGuid().ToString() };
        var computingTask = _computingClient.SendMessage(message);
        var systemTask = _systemClient.SendMessage(message);
        var computingBaseTask = proxy.AddFloat(1, 2);
        await Task.WhenAll(computingTask, systemTask, computingBaseTask);
        systemTask.Result.ShouldBe($"{Environment.UserName}_{_systemCallback.Id}_{message.Text}");
        computingTask.Result.ShouldBe($"{Environment.UserName}_{_computingCallback.Id}_{message.Text}");
        computingBaseTask.Result.ShouldBe(3);
    }

    [Fact]
    public async Task MissingCallback()
    {
        RemoteException exception = null;
        try
        {
            await _systemClient.MissingCallback(new SystemMessage());
        }
        catch (RemoteException ex)
        {
            exception = ex;
        }
        exception.Message.ShouldBe("Callback contract mismatch. Requested System.IDisposable, but it's UiPath.Ipc.Tests.ISystemCallback.");
        exception.Is<ArgumentException>().ShouldBeTrue();
    }
    [Fact]
    public Task CancelServerCall() => CancelServerCallCore(10);

    async Task CancelServerCallCore(int counter)
    {
        for (int i = 0; i < counter; i++)
        {
            var request = new SystemMessage { RequestTimeout = Timeout.InfiniteTimeSpan, Delay = Timeout.Infinite };
            Task sendMessageResult;
            using (var cancellationSource = new CancellationTokenSource())
            {
                sendMessageResult = _systemClient.MissingCallback(request, cancellationSource.Token);
                var newGuid = Guid.NewGuid();
                (await _systemClient.GetGuid(newGuid)).ShouldBe(newGuid);
                await Task.Delay(1);
                cancellationSource.Cancel();
                sendMessageResult.ShouldThrow<Exception>();
                newGuid = Guid.NewGuid();
                (await _systemClient.GetGuid(newGuid)).ShouldBe(newGuid);
            }
            ((IDisposable)_systemClient).Dispose();
        }
    }

    [Fact]
    public async Task DuplicateCallbackProxies()
    {
        await _systemClient.GetThreadName();
        var proxy = CreateSystemService();
        var message = proxy.GetThreadName().ShouldThrow<InvalidOperationException>().Message;
        message.ShouldStartWith("Duplicate callback proxy instance EndpointTests");
        message.ShouldEndWith("<ISystemService, ISystemCallback>. Consider using a singleton callback proxy.");
    }
}
public interface ISystemCallback
{
    Task<string> GetId(Message message = null);
}
public class SystemCallback : ISystemCallback
{
    public string Id { get; set; }
    public async Task<string> GetId(Message message)
    {
        message.Client.ShouldBeNull();
        return Id;
    }
}