namespace UiPath.Rpc.Tests;

public abstract class ComputingTests<TBuilder> : TestBase where TBuilder : ServiceClientBuilder<TBuilder, IComputingService>
{
    protected readonly ServiceHost _computingHost;
    protected readonly IComputingService _computingClient;
    protected readonly ComputingService _computingService;
    protected readonly ComputingCallback _computingCallback;
    public ComputingTests()
    {
        _computingCallback = new ComputingCallback { Id = Guid.NewGuid().ToString() };
        _computingService = (ComputingService)_serviceProvider.GetService<IComputingService>();
        _computingHost = Configure(new ServiceHostBuilder(_serviceProvider))
            .AddEndpoint<IComputingService, IComputingCallback>()
            .ValidateAndBuild();
        _computingHost.RunAsync(GuiScheduler);
        _computingClient = ComputingClientBuilder(GuiScheduler).ValidateAndBuild();
    }
    protected abstract TBuilder ComputingClientBuilder(TaskScheduler taskScheduler = null);
    [Fact]
    public async Task ReconnectWithEncrypt()
    {
        for (int i = 0; i < 50; i++)
        {
            await _computingClient.AddFloat(1, 2);
            ((IpcProxy)_computingClient).CloseConnection();
            await _computingClient.AddFloat(1, 2);
        }
    }

    [Fact]
    public async Task AddFloat()
    {
        var result = await _computingClient.AddFloat(1.23f, 4.56f);
        result.ShouldBe(5.79f);
    }

    [Fact]
    public Task AddFloatConcurrently() => Task.WhenAll(Enumerable.Range(1, 100).Select(_ => AddFloat()));

    [Fact]
    public async Task AddComplexNumber()
    {
        var result = await _computingClient.AddComplexNumber(new ComplexNumber(1f, 3f), new ComplexNumber(2f, 5f));
        result.ShouldBe(new ComplexNumber(3f, 8f));
    }

    [Fact]
    public async Task ClientCancellation()
    {
        using (var cancellationSource = new CancellationTokenSource(10))
        {
            _computingClient.Infinite(cancellationSource.Token).ShouldThrow<TaskCanceledException>();
        }
        await AddFloat();
    }

    [Fact]
    public async Task ClientTimeout()
    {
        var proxy = ComputingClientBuilder().RequestTimeout(TimeSpan.FromMilliseconds(10)).ValidateAndBuild();
        proxy.Infinite().ShouldThrow<TimeoutException>().Message.ShouldBe($"{nameof(_computingClient.Infinite)} timed out.");
        await proxy.GetCallbackThreadName(new Message { RequestTimeout = RequestTimeout });
        ((IDisposable)proxy).Dispose();
        ((IpcProxy)proxy).CloseConnection();
    }

    [Fact]
    public async Task TimeoutPerRequest()
    {
        for (int i = 0; i < 20; i++)
        {
            var request = new SystemMessage { RequestTimeout = TimeSpan.FromTicks(10), Delay = 100 };
            Exception exception = null;
            try
            {
                await _computingClient.SendMessage(request);
            }
            catch (TimeoutException ex)
            {
                exception = ex;
            }
            catch (RemoteException ex)
            {
                exception = ex;
                ex.Is<TimeoutException>().ShouldBeTrue();
            }
            exception.Message.ShouldBe($"{nameof(_computingClient.SendMessage)} timed out.");
            await AddFloat();
        }
    }

    [Fact]
    public Task InfiniteVoid() => _computingClient.InfiniteVoid();

    [Fact]
    public async Task AddComplexNumbers()
    {
        var result = await _computingClient.AddComplexNumbers(new[]
        {
            new ComplexNumber(0.5f, 0.4f),
            new ComplexNumber(0.2f, 0.1f),
            new ComplexNumber(0.3f, 0.5f),
        });
        result.ShouldBe(new ComplexNumber(1f, 1f));
    }

    [Fact]
    public async Task GetCallbackThreadName() => (await _computingClient.GetCallbackThreadName()).ShouldBe("GuiThread");

    [Fact]
    public Task CallbackConcurrently() => Task.WhenAll(Enumerable.Range(1, 50).Select(_ => Callback()));

    [Fact]
    public async Task Callback()
    {
        var message = new SystemMessage { Text = Guid.NewGuid().ToString() };
        var returnValue = await _computingClient.SendMessage(message);
        returnValue.ShouldBe($"{Environment.UserName}_{_computingCallback.Id}_{message.Text}");
    }

    public override void Dispose()
    {
        ((IDisposable)_computingClient).Dispose();
        ((IpcProxy)_computingClient).CloseConnection();
        _computingHost.Dispose();
        base.Dispose();
    }
}