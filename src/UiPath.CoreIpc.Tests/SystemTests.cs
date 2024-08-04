using System.Text;
using UiPath.Ipc.BackCompat;

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
    protected override TConfig Configure<TConfig>(TConfig listenerConfig)
    => base.Configure(listenerConfig) with
    {
        ConcurrentAccepts = 10,
        RequestTimeout = RequestTimeout.Subtract(TimeSpan.FromSeconds(1))
    };

    public override async Task DisposeAsync()
    {
        ((IDisposable)_systemClient).Dispose();
        await ((IpcProxy)_systemClient).CloseConnection();

        await _systemHost.DisposeAsync();
        await base.DisposeAsync();
    }
    [Fact]
    // DONE
    public async Task ConcurrentRequests()
    {
        var infinite = _systemClient.Infinite();
        await Guid();
        infinite.IsCompleted.ShouldBeFalse();
    }
    [Fact]
    // DONE
    public async Task OptionalMessage()
    {
        var returnValue = await _systemClient.ImpersonateCaller();
        returnValue.ShouldBe(Environment.UserName);
    }

    [Fact]
    // DONE
    public async Task ServerTimeout()
    {
        var ex = _systemClient.Infinite().ShouldThrow<RemoteException>();
        ex.Message.ShouldBe($"{nameof(_systemClient.Infinite)} timed out.");
        ex.Is<TimeoutException>().ShouldBeTrue();
        await Guid();
    }
    [Fact]
    // DONE
    public async Task Void()
    {
        _systemService.FireAndForgetDone = false;
        await _systemClient.FireAndForget();
        _systemService.FireAndForgetDone.ShouldBeFalse();
        while (!_systemService.FireAndForgetDone)
        {
            await Task.Delay(10);
            Trace.WriteLine(this + " Void");
        }
    }

    [Fact]
    // WHAT?
    public async Task VoidThreadName()
    {
        await _systemClient.VoidThreadName();
        _ = await _systemClient.GetThreadName();
        while (_systemService.ThreadName != "GuiThread")
        {
            await Task.Delay(0);
            Trace.WriteLine(this + " VoidThreadName");
        }
    }

    [Fact]
    // WHAT?
    public async Task Enum()
    {
        var text = await _systemClient.ConvertText("hEllO woRd!", TextStyle.Upper);
        text.ShouldBe("HELLO WORD!");
    }

    [Fact]
    // WHAT?
    public async Task PropertyWithTypeDefaultValue()
    {
        var args = new ConvertTextArgs { Text = "hEllO woRd!", TextStyle = default };
        var text = await _systemClient.ConvertTextWithArgs(args);
        text.ShouldBe("Hello Word!");
    }

    [Fact]
    // DONE
    public async Task MaxMessageSize()
    {
        _systemClient.ReverseBytes(new byte[MaxReceivedMessageSizeInMegabytes * 1024 * 1024]).ShouldThrow<Exception>();
        await Guid();
    }

    [Fact]
    // DONE
    public async Task Guid()
    {
        var newGuid = System.Guid.NewGuid();
        var guid = await _systemClient.EchoGuid(newGuid);
        guid.ShouldBe(newGuid);
    }

    [Fact]
    // NOT GOING TO PORT
    public Task LargeMessage() => _systemClient.ReverseBytes(new byte[(int)(0.7 * MaxReceivedMessageSizeInMegabytes * 1024 * 1024)]);

    [Fact]
    // WHAT?
    public async Task ReverseBytes()
    {
        var input = Encoding.UTF8.GetBytes("Test");
        var reversed = await _systemClient.ReverseBytes(input);
        reversed.ShouldBe(input.Reverse());
    }

    [Fact]
    // DONE
    public async Task UnexpectedCallback()
    {
        RemoteException exception = null;
        try
        {
            await _systemClient.UnexpectedCallback(new SystemMessage());
        }
        catch (RemoteException ex)
        {
            exception = ex;
        }
        exception.Is<EndpointNotFoundException>().ShouldBeTrue();
        await Guid();
    }


    [Fact]
    // DONE
    public async Task VoidIsAsync() => await _systemClient.VoidSyncThrow();

    [Fact]
    // DONE
    public async Task GetThreadName() => (await _systemClient.GetThreadName()).ShouldBe("GuiThread");

    [Fact]
    // WILL NOT PORT
    public async Task Echo()
    {
        using var stream = await _systemClient.Echo(new MemoryStream(Encoding.UTF8.GetBytes("Hello world")));
        (await new StreamReader(stream).ReadToEndAsync()).ShouldBe("Hello world");
    }

    [Fact]
    // DONE
    public async Task CancelUpload()
    {
        var stream = new MemoryStream(Enumerable.Range(1, 50000).Select(i => (byte)i).ToArray());
        await _systemClient.GetThreadName();
        using (var cancellationSource = new CancellationTokenSource(5))
        {
            _systemClient.Upload(stream, 20, cancellationSource.Token).ShouldThrow<Exception>();
        }
    }

    [Fact]
    // DONE
    public async Task Upload()
    {
        (await _systemClient.Upload(new MemoryStream(Encoding.UTF8.GetBytes("Hello world")))).ShouldBe("Hello world");
        await Guid();
    }

    [Fact]
    // DONE
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
    // WHAT?
    public Task DownloadUiThread() => Task.Factory.StartNew(Download, default, TaskCreationOptions.DenyChildAttach, GuiScheduler).Unwrap();
    [Fact]
    // DONE
    public async Task Download()
    {
        using var stream = await _systemClient.Download("Hello world");
        (await new StreamReader(stream).ReadToEndAsync()).ShouldBe("Hello world");
    }
    [Fact]
    // DONE
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
            c.Method.ShouldBe(typeof(ISystemService).GetMethod(nameof(ISystemService.FireAndForget)));
            c.Arguments.Single().ShouldBe(""); // cancellation token
        }).ValidateAndBuild();
        newConnection.ShouldBeFalse();

        await proxy.FireAndForget();
        newConnection.ShouldBeTrue();

        await proxy.FireAndForget();
        newConnection.ShouldBeFalse();
        var ipcProxy = (IpcProxy)proxy;
        var closed = false;
        ipcProxy.ConnectionClosed += delegate { closed = true; };
        await ipcProxy.CloseConnection();
        closed.ShouldBeTrue();
        newConnection.ShouldBeFalse();
        await proxy.FireAndForget();
        newConnection.ShouldBeTrue();

        await proxy.FireAndForget();
        newConnection.ShouldBeFalse();
        await ipcProxy.CloseConnection();
    }

    [Fact]
    public async Task DontReconnect()
    {
        var proxy = SystemClientBuilder().DontReconnect().ValidateAndBuild();
        await proxy.EchoGuid(System.Guid.Empty);
        await ((IpcProxy)proxy).CloseConnection();
        ObjectDisposedException exception = null;
        try
        {
            await proxy.EchoGuid(System.Guid.Empty);
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
            var sendMessageResult = _systemClient.UnexpectedCallback(request);
            var newGuid = System.Guid.NewGuid();
            (await _systemClient.EchoGuid(newGuid)).ShouldBe(newGuid);
            await Task.Delay(1);
            ((IpcProxy)_systemClient).CloseConnection();
            sendMessageResult.ShouldThrow<Exception>();
            newGuid = System.Guid.NewGuid();
            (await _systemClient.EchoGuid(newGuid)).ShouldBe(newGuid);
        }
    }
    [Fact]
    public async Task ClosingTheHostShouldCloseTheConnection()
    {
        var call = _systemClient.Infinite(new Message { RequestTimeout = Timeout.InfiniteTimeSpan });
        var newGuid = System.Guid.NewGuid();
        (await _systemClient.EchoGuid(newGuid)).ShouldBe(newGuid);
        await Task.Delay(1);
        await _systemHost.DisposeAsync();
        await call.ShouldThrowAsync<Exception>();
    }
    [Fact]
    public virtual async void BeforeCallServerSide()
    {
        var newGuid = System.Guid.NewGuid();
        MethodInfo method = null;
        await using var protectedService = Configure(new ServiceHostBuilder(_serviceProvider))
            .AddEndpoint(new EndpointSettings<ISystemService>()
            {
                BeforeCall = async (call, ct) =>
                {
                    method = call.Method;
                    call.Arguments[0].ShouldBe(newGuid);
                }
            })
            .ValidateAndBuild();
        _ = protectedService.RunAsync();
        await CreateSystemService().EchoGuid(newGuid);
        method.ShouldBe(typeof(ISystemService).GetMethod(nameof(ISystemService.EchoGuid)));
    }
}