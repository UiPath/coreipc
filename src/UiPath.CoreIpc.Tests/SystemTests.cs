using System.Text;

namespace UiPath.Ipc.Tests;

public abstract class SystemTests<TBuilder> : TestBase where TBuilder : ServiceClientBuilder<TBuilder, ISystemService>
{
    protected ServiceHost _systemHost;
    protected readonly ISystemService _systemClient;
    protected readonly SystemService _systemService;
    public SystemTests()
    {
        _systemService = (SystemService)_serviceProvider.GetService<ISystemService>();
        _systemHost = Configure(new ServiceHostBuilder(_serviceProvider))
            .AddEndpoint<ISystemService>()
            .ValidateAndBuild();
        _systemHost.RunAsync(GuiScheduler);
        _systemClient = CreateSystemService();
    }
    protected override TSettings Configure<TSettings>(TSettings listenerSettings)
    {
        base.Configure(listenerSettings);
        listenerSettings.ConcurrentAccepts = 10;
        listenerSettings.RequestTimeout = RequestTimeout.Subtract(TimeSpan.FromSeconds(1));
        return listenerSettings;
    }
    public override void Dispose()
    {
        ((IDisposable)_systemClient).Dispose();
        ((IpcProxy)_systemClient).CloseConnection();
        _systemHost.Dispose();
        base.Dispose();
    }
    [Fact]
    public async Task ConcurrentRequests()
    {
        var infinite = _systemClient.Infinite();
        await Guid();
        infinite.IsCompleted.ShouldBeFalse();
    }
    [Fact]
    public async Task OptionalMessage()
    {
        var returnValue = await _systemClient.ImpersonateCaller();
        returnValue.ShouldBe(Environment.UserName);
    }

    [Fact]
    public async Task ServerTimeout()
    {
        var ex = _systemClient.Infinite().ShouldThrow<RemoteException>();
        ex.Message.ShouldBe($"{nameof(_systemClient.Infinite)} timed out.");
        ex.Is<TimeoutException>().ShouldBeTrue();
        await Guid();
    }
    [Fact]
    public async Task Void()
    {
        _systemService.DidNothing = false;
        await _systemClient.DoNothing();
        _systemService.DidNothing.ShouldBeFalse();
        while (!_systemService.DidNothing)
        {
            await Task.Delay(10);
            Trace.WriteLine(this + " Void");
        }
    }

    [Fact]
    public async Task VoidThreadName()
    {
        await _systemClient.VoidThreadName();
        await _systemClient.GetThreadName();
        while (_systemService.ThreadName != "GuiThread")
        {
            await Task.Delay(0);
            Trace.WriteLine(this + " VoidThreadName");
        }
    }

    [Fact]
    public async Task Enum()
    {
        var text = await _systemClient.ConvertText("hEllO woRd!", TextStyle.Upper);
        text.ShouldBe("HELLO WORD!");
    }

    [Fact]
    public async Task PropertyWithTypeDefaultValue()
    {
        var args = new ConvertTextArgs { Text = "hEllO woRd!", TextStyle = default };
        var text = await _systemClient.ConvertTextWithArgs(args);
        text.ShouldBe("Hello Word!");
    }

    [Fact]
    public async Task MaxMessageSize()
    {
        _systemClient.ReverseBytes(new byte[MaxReceivedMessageSizeInMegabytes * 1024 * 1024]).ShouldThrow<Exception>();
        await Guid();
    }

    [Fact]
    public async Task Guid()
    {
        var newGuid = System.Guid.NewGuid();
        var guid = await _systemClient.GetGuid(newGuid);
        guid.ShouldBe(newGuid);
    }

    [Fact]
    public Task LargeMessage() => _systemClient.ReverseBytes(new byte[(int)(0.7 * MaxReceivedMessageSizeInMegabytes * 1024 * 1024)]);

    [Fact]
    public async Task ReverseBytes()
    {
        var input = Encoding.UTF8.GetBytes("Test");
        var reversed = await _systemClient.ReverseBytes(input);
        reversed.ShouldBe(input.Reverse());
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
        exception.Message.ShouldBe("Callback contract mismatch. Requested System.IDisposable, but it's not configured.");
        exception.Is<InvalidOperationException>().ShouldBeTrue();
        await Guid();
    }


    [Fact]
    public async Task VoidIsAsync() => await _systemClient.VoidSyncThrow();

    [Fact]
    public async Task GetThreadName() => (await _systemClient.GetThreadName()).ShouldBe("GuiThread");

    [Fact]
    public async Task Echo()
    {
        using var stream = await _systemClient.Echo(new MemoryStream(Encoding.UTF8.GetBytes("Hello world")));
        (await new StreamReader(stream).ReadToEndAsync()).ShouldBe("Hello world");
    }

    [Fact]
    public async Task CancelUpload()
    {
        var stream = new MemoryStream(Enumerable.Range(1, 50000).Select(i=>(byte)i).ToArray());
        await _systemClient.GetThreadName();
        using (var cancellationSource = new CancellationTokenSource(5))
        {
            _systemClient.Upload(stream, 20, cancellationSource.Token).ShouldThrow<Exception>();
        }
    }

    [Fact]
    public async Task Upload()
    {
        (await _systemClient.Upload(new MemoryStream(Encoding.UTF8.GetBytes("Hello world")))).ShouldBe("Hello world");
        await Guid();
    }

    [Fact]
    public virtual async Task UploadNoRead()
    {
        try
        {
            (await _systemClient.UploadNoRead(new MemoryStream(Encoding.UTF8.GetBytes("Hello world")))).ShouldBeEmpty();
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        await Guid();
    }

    [Fact]
    public Task DownloadUiThread() => Task.Factory.StartNew(Download, default, TaskCreationOptions.DenyChildAttach, GuiScheduler).Unwrap();
    [Fact]
    public async Task Download()
    {
        using var stream = await _systemClient.Download("Hello world");
        (await new StreamReader(stream).ReadToEndAsync()).ShouldBe("Hello world");
    }
    [Fact]
    public async Task DownloadNoRead()
    {
        using (await _systemClient.Download("Hello world")) { }
        await Guid();
    }
    protected abstract TBuilder CreateSystemClientBuilder();
    protected TBuilder SystemClientBuilder() => CreateSystemClientBuilder().RequestTimeout(RequestTimeout).Logger(_serviceProvider);
    [Fact]
    public async Task BeforeCall()
    {
        bool newConnection = false;
        var proxy = SystemClientBuilder().BeforeCall(async (c, _) =>
        {
            newConnection = c.NewConnection;
            c.Method.ShouldBe(typeof(ISystemService).GetMethod(nameof(ISystemService.DoNothing)));
            c.Arguments.Single().ShouldBe(""); // cancellation token
        }).ValidateAndBuild();
        newConnection.ShouldBeFalse();

        await proxy.DoNothing();
        newConnection.ShouldBeTrue();

        await proxy.DoNothing();
        newConnection.ShouldBeFalse();
        var ipcProxy = (IpcProxy)proxy;
        var closed = false;
        ipcProxy.Connection.Closed += delegate { closed = true; };
        ipcProxy.CloseConnection();
        closed.ShouldBeTrue();
        newConnection.ShouldBeFalse();
        await proxy.DoNothing();
        newConnection.ShouldBeTrue();

        await proxy.DoNothing();
        newConnection.ShouldBeFalse();
        ipcProxy.CloseConnection();
    }

    [Fact]
    public async Task DontReconnect()
    {
        var proxy = SystemClientBuilder().DontReconnect().ValidateAndBuild();
        await proxy.GetGuid(System.Guid.Empty);
        ((IpcProxy)proxy).CloseConnection();
        ObjectDisposedException exception = null;
        try
        {
            await proxy.GetGuid(System.Guid.Empty);
        }
        catch (ObjectDisposedException ex)
        {
            exception = ex;
        }
        exception.ShouldNotBeNull();
    }
    [Fact]
    public Task CancelServerCall() => CancelServerCallCore(10);
    protected ISystemService CreateSystemService() => SystemClientBuilder().ValidateAndBuild();

    async Task CancelServerCallCore(int counter)
    {
        for (int i = 0; i < counter; i++)
        {
            var request = new SystemMessage { RequestTimeout = Timeout.InfiniteTimeSpan, Delay = Timeout.Infinite };
            var sendMessageResult = _systemClient.MissingCallback(request);
            var newGuid = System.Guid.NewGuid();
            (await _systemClient.GetGuid(newGuid)).ShouldBe(newGuid);
            await Task.Delay(1);
            ((IpcProxy)_systemClient).CloseConnection();
            sendMessageResult.ShouldThrow<Exception>();
            newGuid = System.Guid.NewGuid();
            (await _systemClient.GetGuid(newGuid)).ShouldBe(newGuid);
        }
    }
    [Fact]
    public async Task ClosingTheHostShouldCloseTheConnection()
    {
        var request = new SystemMessage { RequestTimeout = Timeout.InfiniteTimeSpan, Delay = Timeout.Infinite };
        var sendMessageResult = _systemClient.MissingCallback(request);
        var newGuid = System.Guid.NewGuid();
        (await _systemClient.GetGuid(newGuid)).ShouldBe(newGuid);
        await Task.Delay(1);
        _systemHost.Dispose();
        sendMessageResult.ShouldThrow<Exception>();
    }
    [Fact]
    public virtual async void BeforeCallServerSide()
    {
        var newGuid = System.Guid.NewGuid();
        MethodInfo method = null;
        using var protectedService = Configure(new ServiceHostBuilder(_serviceProvider))
            .AddEndpoint(new EndpointSettings<ISystemService>
            {
                BeforeCall = async (call, ct) =>
                {
                    method = call.Method;
                    call.Arguments[0].ShouldBe(newGuid);
                }
            })
            .ValidateAndBuild();
        _ = protectedService.RunAsync();
        await CreateSystemService().GetGuid(newGuid);
        method.ShouldBe(typeof(ISystemService).GetMethod(nameof(ISystemService.GetGuid)));
    }
}