using Newtonsoft.Json.Linq;

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

public interface IErrorDataRelay
{
    Task<bool> Rethrow();
    Task<bool> Wrap();
}

public sealed class ErrorDataRelay(IErrorDataProbe next) : IErrorDataRelay
{
    public Task<bool> Rethrow() => next.Throw();

    public async Task<bool> Wrap()
    {
        try
        {
            return await next.Throw();
        }
        catch (RemoteException ex)
        {
            var wrapper = new InvalidOperationException("relay", ex);
            wrapper.Data[ErrorDataProbe.ListedKey2] = "relay-value";
            throw wrapper;
        }
    }
}

public sealed class ExceptionDataTests
{
    private static readonly string[] Keys = [ErrorDataProbe.ListedKey, ErrorDataProbe.ListedKey2];

    [Fact]
    public async Task ListedKey_ReachesTheCaller_UnlistedDoesNot()
    {
        await using var pair = await CreatePair(serverKeys: Keys);
        var ex = await pair.Proxy.Throw().ShouldThrowAsync<RemoteException>();
        ex.Data[ErrorDataProbe.ListedKey].ShouldBe(ErrorDataProbe.Value);
        ex.Data.Contains(ErrorDataProbe.UnlistedKey).ShouldBeFalse();
        ex.Message.ShouldBe("boom");
    }

    [Fact]
    public async Task NoKeys_ByDefault_NothingIsCarried()
    {
        await using var pair = await CreatePair();
        var ex = await pair.Proxy.Throw().ShouldThrowAsync<RemoteException>();
        ex.Data.Count.ShouldBe(0);
    }

    [Fact]
    public async Task InnerExceptions_CarryTheirOwnData()
    {
        await using var pair = await CreatePair(serverKeys: Keys);
        var ex = await pair.Proxy.ThrowWrapped().ShouldThrowAsync<RemoteException>();
        ex.Data[ErrorDataProbe.ListedKey].ShouldBe("outer-value");
        ex.InnerException!.Data[ErrorDataProbe.ListedKey].ShouldBe("inner-value");
        ex.InnerException.Data.Contains(ErrorDataProbe.UnlistedKey).ShouldBeFalse();
    }

    [Fact]
    public async Task AggregateException_InnerDataIsReachable()
    {
        await using var pair = await CreatePair(serverKeys: Keys);
        var ex = await pair.Proxy.ThrowAggregate().ShouldThrowAsync<RemoteException>();
        ex.Is<AggregateException>().ShouldBeTrue();
        ex.InnerException!.Data[ErrorDataProbe.ListedKey].ShouldBe("inner-value");
    }

    [Fact]
    public async Task NonStringValue_IsSkipped_WithoutBreakingTheError()
    {
        await using var pair = await CreatePair(serverKeys: Keys);
        var ex = await pair.Proxy.ThrowNonString().ShouldThrowAsync<RemoteException>();
        ex.Message.ShouldBe("non-string");
        ex.Data.Contains(ErrorDataProbe.ListedKey).ShouldBeFalse();
        ex.Data[ErrorDataProbe.ListedKey2].ShouldBe("ok");
    }

    [Fact]
    public async Task Timeout_KeepsTheOriginalsDataReachable()
    {
        await using var pair = await CreatePair(serverKeys: Keys, serverTimeout: TimeSpan.FromMilliseconds(300));
        var ex = await pair.Proxy.HangThenThrow().ShouldThrowAsync<RemoteException>();
        ex.Is<TimeoutException>().ShouldBeTrue();
        ex.InnerException!.Data[ErrorDataProbe.ListedKey].ShouldBe(ErrorDataProbe.Value);
    }

    [Fact]
    public async Task Callback_ClientKeys_ReachTheServerSideCaller()
    {
        await using var pair = await CreatePair(clientKeys: Keys);
        (await pair.Proxy.CallFailingCallback()).ShouldBe("callback-value|False");
    }

    [Fact]
    public async Task Callback_ServerKeysDoNotApplyToTheClientsErrors()
    {
        await using var pair = await CreatePair(serverKeys: Keys);
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
    public void FromException_FaultyEntry_KeepsTheEntriesGatheredBeforeIt()
    {
        var error = Error.FromException(new FaultyEntryException(), [ErrorDataProbe.ListedKey2, ErrorDataProbe.ListedKey]);
        error.Message.ShouldBe("faulty entry");
        error.Data!.Keys.ShouldBe(new[] { ErrorDataProbe.ListedKey2 });
        error.Data[ErrorDataProbe.ListedKey2].ShouldBe("ok");
    }

    [Fact]
    public void FromException_RemoteException_PassesOnWhatItReceived_ListedKeysCurrentValueWins()
    {
        var remote = new RemoteException(new Error("m", "s", "t", InnerError: new Error("i", "s", "t", InnerError: null) { Data = new Dictionary<string, string> { ["k2"] = "received" } })
        {
            Data = new Dictionary<string, string> { ["k"] = "received", [ErrorDataProbe.ListedKey] = "received" }
        });
        remote.Data.Remove("k");
        remote.Data[ErrorDataProbe.ListedKey] = "changed";

        var error = Error.FromException(remote, Keys);

        error.Data!["k"].ShouldBe("received");
        error.Data[ErrorDataProbe.ListedKey].ShouldBe("changed");
        error.InnerError!.Data!["k2"].ShouldBe("received");
        Error.FromException(remote).Data!.Keys.ShouldBe(new[] { "k", ErrorDataProbe.ListedKey }, ignoreOrder: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FromException_ListedKeyWithoutACurrentStringValue_IsDropped(bool setToNonString)
    {
        var remote = new RemoteException(new Error("m", "s", "t", InnerError: null)
        {
            Data = new Dictionary<string, string> { [ErrorDataProbe.ListedKey] = "received", ["k"] = "received" }
        });
        if (setToNonString)
        {
            remote.Data[ErrorDataProbe.ListedKey] = 42;
        }
        else
        {
            remote.Data.Remove(ErrorDataProbe.ListedKey);
        }

        Error.FromException(remote, Keys).Data!.Keys.ShouldBe(new[] { "k" });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Chain_RethrownAsIs_CsEntriesReachA(bool bListsAnotherKey)
    {
        await using var c = await CreatePair(serverKeys: [ErrorDataProbe.ListedKey]);
        await using var b = await CreateRelay(c.Proxy, bListsAnotherKey ? [ErrorDataProbe.ListedKey2] : null);

        var ex = await b.Proxy.Rethrow().ShouldThrowAsync<RemoteException>();

        ex.Message.ShouldBe("boom");
        ex.Data[ErrorDataProbe.ListedKey].ShouldBe(ErrorDataProbe.Value);
        ex.Data.Contains(ErrorDataProbe.UnlistedKey).ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Chain_WrappedAsInner_CsEntriesReachA_NextToBsOwn(bool bListsItsOwnKey)
    {
        await using var c = await CreatePair(serverKeys: [ErrorDataProbe.ListedKey]);
        await using var b = await CreateRelay(c.Proxy, bListsItsOwnKey ? [ErrorDataProbe.ListedKey2] : null);

        var ex = await b.Proxy.Wrap().ShouldThrowAsync<RemoteException>();

        ex.Message.ShouldBe("relay");
        ex.Data.Contains(ErrorDataProbe.ListedKey).ShouldBeFalse();
        ex.Data.Contains(ErrorDataProbe.ListedKey2).ShouldBe(bListsItsOwnKey);
        ex.InnerException!.Data[ErrorDataProbe.ListedKey].ShouldBe(ErrorDataProbe.Value);
        ex.InnerException.Data.Contains(ErrorDataProbe.ListedKey2).ShouldBeFalse();
    }

    [Fact]
    public async Task Chain_NothingListedAnywhere_NothingTravels()
    {
        await using var c = await CreatePair();
        await using var b = await CreateRelay(c.Proxy, serverKeys: null);

        (await b.Proxy.Rethrow().ShouldThrowAsync<RemoteException>()).Data.Count.ShouldBe(0);
        var wrapped = await b.Proxy.Wrap().ShouldThrowAsync<RemoteException>();
        wrapped.Data.Count.ShouldBe(0);
        wrapped.InnerException!.Data.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Keys_AreCopiedWhenTheConnectionIsEstablished()
    {
        var keys = new List<string>(Keys);
        await using var pair = await CreatePair(serverKeys: keys);
        await pair.Proxy.Throw().ShouldThrowAsync<RemoteException>();
        keys.Clear();
        var ex = await pair.Proxy.Throw().ShouldThrowAsync<RemoteException>();
        ex.Data[ErrorDataProbe.ListedKey].ShouldBe(ErrorDataProbe.Value);
    }

    [Fact]
    public async Task ClosedConnection_FailsEachPendingCallWithItsOwnException()
    {
        var connection = new Connection(new MemoryStream(), "test", logger: null);
        connection.Closed += delegate { };
        using var timeout = new CancellationTokenSource(Timeouts.DefaultRequest);
        var first = connection.RemoteCall(Request("1"), timeout.Token).AsTask();
        var second = connection.RemoteCall(Request("2"), timeout.Token).AsTask();
        connection.Dispose();

        var firstException = await first.ShouldThrowAsync<IOException>();
        firstException.Data[ErrorDataProbe.ListedKey] = "tagged";
        var secondException = await second.ShouldThrowAsync<IOException>();
        secondException.ShouldNotBeSameAs(firstException);
        secondException.Data.Count.ShouldBe(0);
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

    private static Request Request(string id = "1") => new("endpoint", id, "method", [], 0);

    private static JObject WireError(Response response)
        => (JObject)JObject.Parse(IpcJsonSerializer.Instance.Serialize(response))["Error"]!;

    private static Error Deserialize(string json) => (Error)IpcJsonSerializer.Instance.Deserialize(json, typeof(Error))!;

    private static Task<IpcPair<IErrorDataProbe>> CreatePair(IReadOnlyCollection<string>? serverKeys = null, IReadOnlyCollection<string>? clientKeys = null, TimeSpan? serverTimeout = null)
        => IpcPair<IErrorDataProbe>.Create(
            new ErrorDataProbe(),
            server =>
            {
                server.RequestTimeout = serverTimeout;
                server.PropagatedExceptionDataKeys = serverKeys;
            },
            client =>
            {
                client.Callbacks = new() { { typeof(IErrorDataCallback), new ErrorDataCallback() } };
                client.PropagatedExceptionDataKeys = clientKeys;
            });

    private static Task<IpcPair<IErrorDataRelay>> CreateRelay(IErrorDataProbe next, IReadOnlyCollection<string>? serverKeys)
        => IpcPair<IErrorDataRelay>.Create(new ErrorDataRelay(next), server => server.PropagatedExceptionDataKeys = serverKeys);
}
