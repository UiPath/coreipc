using Newtonsoft.Json.Linq;
using UiPath.Ipc.Transport.NamedPipe;

namespace UiPath.Ipc.Tests;

public interface IErrorDataProbe
{
    Task<bool> Throw();
    Task<bool> ThrowWrapped();
    Task<bool> ThrowAggregate();
    Task<bool> ThrowNonString();
    Task<bool> HangThenThrow(CancellationToken ct = default);
    Task<string> CallFailingCallback();
}

public interface IErrorDataCallback
{
    Task<bool> Throw();
}

public sealed class ErrorDataProbe : IErrorDataProbe
{
    public const string ListedKey = "UiPath.ErrorInfo.Error";
    public const string ListedKey2 = "Listed2";
    public const string UnlistedKey = "Unlisted";
    public const string Value = """{"Code":"Robot1001"}""";

    public static TException WithData<TException>(TException exception, string value = Value) where TException : Exception
    {
        exception.Data[ListedKey] = value;
        exception.Data[UnlistedKey] = "unlisted-value";
        return exception;
    }

    public Task<bool> Throw() => throw WithData(new InvalidOperationException("boom"));

    public Task<bool> ThrowWrapped()
        => throw WithData(new InvalidOperationException("outer", WithData(new ArgumentException("inner"), "inner-value")), "outer-value");

    public Task<bool> ThrowAggregate()
        => throw new AggregateException(WithData(new InvalidOperationException("inner"), "inner-value"));

    public Task<bool> ThrowNonString()
    {
        var exception = new InvalidOperationException("non-string");
        exception.Data[ListedKey] = 42;
        exception.Data[ListedKey2] = "ok";
        throw exception;
    }

    public async Task<bool> HangThenThrow(CancellationToken ct)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            throw WithData(new InvalidOperationException("gave up"));
        }
        return true;
    }

    public async Task<string> CallFailingCallback()
    {
        try
        {
            await IpcContext.Current!.GetCallback<IErrorDataCallback>().Throw();
            return "no error";
        }
        catch (RemoteException ex)
        {
            return $"{ex.Data[ListedKey]}|{ex.Data.Contains(UnlistedKey)}";
        }
    }
}

public sealed class ErrorDataCallback : IErrorDataCallback
{
    public Task<bool> Throw() => throw ErrorDataProbe.WithData(new InvalidOperationException("callback boom"), "callback-value");
}

public sealed class ExceptionDataTests
{
    private static readonly string[] Keys = [ErrorDataProbe.ListedKey, ErrorDataProbe.ListedKey2];

    [Fact]
    public async Task ListedKey_ReachesTheCaller_UnlistedDoesNot()
    {
        await using var pair = await Pair.Create(serverKeys: Keys);
        var ex = await pair.Proxy.Throw().ShouldThrowAsync<RemoteException>();
        ex.Data[ErrorDataProbe.ListedKey].ShouldBe(ErrorDataProbe.Value);
        ex.Data.Contains(ErrorDataProbe.UnlistedKey).ShouldBeFalse();
        ex.Message.ShouldBe("boom");
    }

    [Fact]
    public async Task NoKeys_ByDefault_NothingIsCarried()
    {
        await using var pair = await Pair.Create();
        var ex = await pair.Proxy.Throw().ShouldThrowAsync<RemoteException>();
        ex.Data.Count.ShouldBe(0);
    }

    [Fact]
    public async Task InnerExceptions_CarryTheirOwnData()
    {
        await using var pair = await Pair.Create(serverKeys: Keys);
        var ex = await pair.Proxy.ThrowWrapped().ShouldThrowAsync<RemoteException>();
        ex.Data[ErrorDataProbe.ListedKey].ShouldBe("outer-value");
        ex.InnerException!.Data[ErrorDataProbe.ListedKey].ShouldBe("inner-value");
        ex.InnerException.Data.Contains(ErrorDataProbe.UnlistedKey).ShouldBeFalse();
    }

    [Fact]
    public async Task AggregateException_InnerDataIsReachable()
    {
        await using var pair = await Pair.Create(serverKeys: Keys);
        var ex = await pair.Proxy.ThrowAggregate().ShouldThrowAsync<RemoteException>();
        ex.Is<AggregateException>().ShouldBeTrue();
        ex.InnerException!.Data[ErrorDataProbe.ListedKey].ShouldBe("inner-value");
    }

    [Fact]
    public async Task NonStringValue_IsSkipped_WithoutBreakingTheError()
    {
        await using var pair = await Pair.Create(serverKeys: Keys);
        var ex = await pair.Proxy.ThrowNonString().ShouldThrowAsync<RemoteException>();
        ex.Message.ShouldBe("non-string");
        ex.Data.Contains(ErrorDataProbe.ListedKey).ShouldBeFalse();
        ex.Data[ErrorDataProbe.ListedKey2].ShouldBe("ok");
    }

    [Fact]
    public async Task Timeout_KeepsTheOriginalsDataReachable()
    {
        await using var pair = await Pair.Create(serverKeys: Keys, serverTimeout: TimeSpan.FromMilliseconds(300));
        var ex = await pair.Proxy.HangThenThrow().ShouldThrowAsync<RemoteException>();
        ex.Is<TimeoutException>().ShouldBeTrue();
        ex.InnerException!.Data[ErrorDataProbe.ListedKey].ShouldBe(ErrorDataProbe.Value);
    }

    [Fact]
    public async Task Callback_ClientKeys_ReachTheServerSideCaller()
    {
        await using var pair = await Pair.Create(clientKeys: Keys);
        (await pair.Proxy.CallFailingCallback()).ShouldBe("callback-value|False");
    }

    [Fact]
    public async Task Callback_ServerKeysDoNotApplyToTheClientsErrors()
    {
        await using var pair = await Pair.Create(serverKeys: Keys);
        (await pair.Proxy.CallFailingCallback()).ShouldBe("|False");
    }

    [Fact]
    public void Wire_NoKeys_OmitsData()
    {
        var error = WireError(Response.Fail(Request(), NewWrapped(), dataKeys: null));
        error.ContainsKey("Data").ShouldBeFalse();
        ((JObject)error["InnerError"]!).ContainsKey("Data").ShouldBeFalse();
    }

    [Fact]
    public void Wire_KeysWithoutMatchingValues_OmitData()
    {
        var error = WireError(Response.Fail(Request(), new InvalidOperationException("plain"), Keys));
        error.ContainsKey("Data").ShouldBeFalse();
    }

    [Fact]
    public void Wire_Keys_EmitOnlyTheListedEntries()
    {
        var error = WireError(Response.Fail(Request(), NewWrapped(), Keys));
        error["Data"]!.ToString(Newtonsoft.Json.Formatting.None).ShouldBe($$"""{"{{ErrorDataProbe.ListedKey}}":"outer-value"}""");
        error["InnerError"]!["Data"]![ErrorDataProbe.ListedKey]!.Value<string>().ShouldBe("inner-value");
    }

    [Fact]
    public void OldPeer_ErrorWithoutData_Deserializes()
    {
        var error = Deserialize("""{"Message":"m","StackTrace":"s","Type":"t","InnerError":null}""");
        error.Message.ShouldBe("m");
        error.Data.ShouldBeNull();
        new RemoteException(error).Data.Count.ShouldBe(0);
    }

    [Fact]
    public void NewerPeer_DataAndUnknownProperty_Deserialize()
    {
        var error = Deserialize("""{"Message":"m","StackTrace":"s","Type":"t","InnerError":{"Message":"i","StackTrace":"s","Type":"t","InnerError":null,"Data":{"k2":"v2"}},"Data":{"k":"v"},"Extra":{"x":1}}""");
        error.Data!["k"].ShouldBe("v");
        error.InnerError!.Data!["k2"].ShouldBe("v2");
    }

    [Fact]
    public void Response_RoundTrips()
    {
        var json = IpcJsonSerializer.Instance.Serialize(Response.Fail(Request(), NewWrapped(), Keys));
        var response = (Response)IpcJsonSerializer.Instance.Deserialize(json, typeof(Response))!;
        var ex = Should.Throw<RemoteException>(() => response.Deserialize<bool>());
        ex.Data[ErrorDataProbe.ListedKey].ShouldBe("outer-value");
        ex.InnerException!.Data[ErrorDataProbe.ListedKey].ShouldBe("inner-value");
    }

    [Fact]
    public void FromException_WithKeys_AppliesTheSameRule()
    {
        var error = Error.FromException(NewWrapped(), Keys);
        error.Data!.Keys.ShouldBe(new[] { ErrorDataProbe.ListedKey });
        error.Data[ErrorDataProbe.ListedKey].ShouldBe("outer-value");
        error.InnerError!.Data!.Keys.ShouldBe(new[] { ErrorDataProbe.ListedKey });
        error.InnerError.Data[ErrorDataProbe.ListedKey].ShouldBe("inner-value");
        error.ToString().ShouldNotContain("outer-value");
    }

    [Fact]
    public void FromException_WithoutKeys_CarriesNoData()
    {
        Error.FromException(NewWrapped()).Data.ShouldBeNull();
        Error.FromException(NewWrapped(), []).Data.ShouldBeNull();
        Error.FromException(null, Keys).ShouldBeNull();
    }

    [Fact]
    public void FromException_FaultyDataOverride_DoesNotThrow()
    {
        var error = Error.FromException(new FaultyDataException(), Keys);
        error.Message.ShouldBe("faulty");
        error.Data.ShouldBeNull();
    }

    [Fact]
    public void FromException_FaultyEntry_DoesNotDropTheOtherKeys()
    {
        var error = Error.FromException(new FaultyEntryException(), Keys);
        error.Data!.Keys.ShouldBe(new[] { ErrorDataProbe.ListedKey2 });
        error.Data[ErrorDataProbe.ListedKey2].ShouldBe("ok");
    }

    [Fact]
    public async Task Keys_AreCopiedWhenTheConnectionIsEstablished()
    {
        var keys = new List<string>(Keys);
        await using var pair = await Pair.Create(serverKeys: keys);
        await pair.Proxy.Throw().ShouldThrowAsync<RemoteException>();
        keys.Clear();
        var ex = await pair.Proxy.Throw().ShouldThrowAsync<RemoteException>();
        ex.Data[ErrorDataProbe.ListedKey].ShouldBe(ErrorDataProbe.Value);
    }

    [Fact]
    public void RemoteException_AcceptsTheValues()
    {
        var ex = new RemoteException(new Error("m", "s", "t", InnerError: null) { Data = new Dictionary<string, string> { ["k"] = "v" } });
        ex.Data["k"].ShouldBe("v");
    }

    private sealed class FaultyDataException() : Exception("faulty")
    {
        public override System.Collections.IDictionary Data => throw new NotSupportedException();
    }

    private sealed class FaultyEntryException() : Exception("faulty entry")
    {
        public override System.Collections.IDictionary Data { get; } = new FaultyEntryData { [ErrorDataProbe.ListedKey2] = "ok" };
    }

    private sealed class FaultyEntryData : System.Collections.Hashtable
    {
        public override object? this[object key]
        {
            get => key is ErrorDataProbe.ListedKey ? throw new KeyNotFoundException() : base[key];
            set => base[key] = value;
        }
    }

    private static Exception NewWrapped()
        => ErrorDataProbe.WithData(new InvalidOperationException("outer", ErrorDataProbe.WithData(new ArgumentException("inner"), "inner-value")), "outer-value");

    private static Request Request() => new("endpoint", "1", "method", [], 0);

    private static JObject WireError(Response response)
        => (JObject)JObject.Parse(IpcJsonSerializer.Instance.Serialize(response))["Error"]!;

    private static Error Deserialize(string json) => (Error)IpcJsonSerializer.Instance.Deserialize(json, typeof(Error))!;

    private sealed class Pair : IAsyncDisposable
    {
        private readonly IpcServer _server;
        public IErrorDataProbe Proxy { get; }

        private Pair(IpcServer server, IErrorDataProbe proxy)
        {
            _server = server;
            Proxy = proxy;
        }

        public static async Task<Pair> Create(IReadOnlyCollection<string>? serverKeys = null, IReadOnlyCollection<string>? clientKeys = null, TimeSpan? serverTimeout = null)
        {
            var pipeName = $"ipctest_errdata_{Guid.NewGuid():N}";

            var server = new IpcServer
            {
                Transport = new NamedPipeServerTransport { PipeName = pipeName },
                Endpoints = new() { typeof(IErrorDataProbe) },
                ServiceProvider = new ServiceCollection()
                    .AddLogging()
                    .AddSingleton<IErrorDataProbe, ErrorDataProbe>()
                    .BuildServiceProvider(),
                RequestTimeout = serverTimeout,
                PropagatedExceptionDataKeys = serverKeys,
            };

            var client = new IpcClient
            {
                Transport = new NamedPipeClientTransport { PipeName = pipeName },
                Callbacks = new() { { typeof(IErrorDataCallback), new ErrorDataCallback() } },
                PropagatedExceptionDataKeys = clientKeys,
            };
            var proxy = client.GetProxy<IErrorDataProbe>();

            server.Start();
            await Task.Yield();
            return new Pair(server, proxy);
        }

        public async ValueTask DisposeAsync()
        {
            (Proxy as IpcProxy)?.Dispose();
            await ((Proxy as IpcProxy)?.CloseConnection() ?? default);
            await _server.DisposeAsync();
        }
    }
}
