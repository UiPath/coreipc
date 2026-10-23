using System.IO.Pipes;
using System.Text;
using UiPath.Ipc.Transport.NamedPipe;

namespace UiPath.Ipc.Tests;

public sealed record TraceProbeResult(
    string? TraceParent,
    string? TraceState,
    string? Baggage,
    string? ActivityTraceId,
    string? ActivityParentSpanId,
    string? ActivitySpanId,
    string[]? ActivityBaggage,
    string? OverrideTraceParent)
{
    public static TraceProbeResult Capture()
    {
        var received = IpcContext.Current?.TraceContext;
        var activity = Activity.Current;
        return new(
            received?.TraceParent,
            received?.TraceState,
            received?.Baggage,
            activity?.TraceId.ToHexString(),
            activity?.ParentSpanId.ToHexString(),
            activity?.SpanId.ToHexString(),
            activity?.Baggage.Select(pair => $"{pair.Key}={pair.Value}").ToArray(),
            IpcTelemetry.Context.Current?.TraceParent);
    }
}

public sealed record TraceProbeCallbackResult(TraceProbeResult Server, TraceProbeResult Callback);

public interface ITraceProbe
{
    Task<TraceProbeResult> Probe();
    Task<TraceProbeCallbackResult> ProbeViaCallback(string? overrideTraceParent = null);
    Task<TraceProbeResult> ProbeUpload(Stream stream);
    Task<TraceProbeResult> ProbeUploadViaCallback();
}

public interface ITraceProbeCallback
{
    Task<TraceProbeResult> Probe();
    Task<TraceProbeResult> ProbeUpload(Stream stream);
}

public sealed class TraceProbe : ITraceProbe
{
    public Task<TraceProbeResult> Probe() => Task.FromResult(TraceProbeResult.Capture());

    public async Task<TraceProbeCallbackResult> ProbeViaCallback(string? overrideTraceParent = null)
    {
        var server = TraceProbeResult.Capture();
        var callback = IpcContext.Current!.GetCallback<ITraceProbeCallback>();
        if (overrideTraceParent is null)
        {
            return new(server, await callback.Probe());
        }
        using (IpcTelemetry.Context.Use(new(overrideTraceParent)))
        {
            return new(server, await callback.Probe());
        }
    }

    public Task<TraceProbeResult> ProbeUpload(Stream stream) => Task.FromResult(TraceProbeResult.Capture());

    public Task<TraceProbeResult> ProbeUploadViaCallback()
    => IpcContext.Current!.GetCallback<ITraceProbeCallback>().ProbeUpload(new MemoryStream());
}

public sealed class TraceProbeCallback : ITraceProbeCallback
{
    public Task<TraceProbeResult> Probe() => Task.FromResult(TraceProbeResult.Capture());

    public Task<TraceProbeResult> ProbeUpload(Stream stream) => Task.FromResult(TraceProbeResult.Capture());
}

public sealed class TraceContextTests
{
    private const string TraceParentA = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
    private const string TraceParentB = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-00";
    private const string TraceState = "congo=t61rcWkgMzE,rojo=00f067aa0ba902b7";

    [Fact]
    public async Task ClientPropagationNone_SendsNothing()
    {
        await using var pair = await Pair.Create(clientPropagation: TelemetryPropagationPolicy.None);
        using var activity = StartW3CActivity();
        using (IpcTelemetry.Context.Use(new(TraceParentA)))
        {
            var result = await pair.Proxy.Probe();

            result.TraceParent.ShouldBeNull();
            result.ActivityTraceId.ShouldBeNull();
        }
    }

    [Fact]
    public async Task ActivityCurrent_IsPropagated()
    {
        await using var pair = await Pair.Create();
        using var activity = StartW3CActivity();
        // Freezes the caller's Id with flags 00, so only a client span started after the change can send 01.
        _ = activity.Id;
        activity.ActivityTraceFlags = ActivityTraceFlags.Recorded;
        activity.TraceStateString = TraceState;
        activity.AddBaggage("user", "alice smith");
        activity.AddBaggage("tenant", "a,b");

        var result = await pair.Proxy.Probe();

        var clientSpanId = result.TraceParent!.Substring(36, 16);
        result.TraceParent.ShouldBe($"00-{activity.TraceId.ToHexString()}-{clientSpanId}-01");
        clientSpanId.ShouldNotBe(activity.SpanId.ToHexString());
        result.TraceState.ShouldBe(TraceState);
        result.Baggage.ShouldBe("tenant=a%2Cb,user=alice%20smith");
        result.ActivityTraceId.ShouldBe(activity.TraceId.ToHexString());
        result.ActivityParentSpanId.ShouldBe(clientSpanId);
        result.ActivitySpanId.ShouldNotBe(clientSpanId);
        result.ActivityBaggage.ShouldBe(["tenant=a,b", "user=alice smith"]);
    }

    [Fact]
    public async Task HierarchicalActivity_IsNotPropagated()
    {
        await using var pair = await Pair.Create();
        using var activity = new Activity("caller").SetIdFormat(ActivityIdFormat.Hierarchical).Start();

        (await pair.Proxy.Probe()).TraceParent.ShouldBeNull();
    }

    [Fact]
    public async Task NoAmbientContext_SendsNothing()
    {
        await using var pair = await Pair.Create();

        var result = await pair.Proxy.Probe();

        result.TraceParent.ShouldBeNull();
        result.ActivityTraceId.ShouldBeNull();
    }

    [Fact]
    public async Task ExplicitOverride_WinsOverActivity()
    {
        await using var pair = await Pair.Create();
        using var activity = StartW3CActivity();

        using (IpcTelemetry.Context.Use(new(TraceParentA, TraceState, "k=v")))
        {
            var result = await pair.Proxy.Probe();

            result.TraceParent.ShouldBe(TraceParentA);
            result.TraceState.ShouldBe(TraceState);
            result.Baggage.ShouldBe("k=v");
            result.ActivityTraceId.ShouldBe("4bf92f3577b34da6a3ce929d0e0e4736");
            result.ActivityParentSpanId.ShouldBe("00f067aa0ba902b7");
            result.ActivityBaggage.ShouldBe(["k=v"]);
        }
    }

    [Fact]
    public async Task OverrideScopes_NestAndRestore()
    {
        await using var pair = await Pair.Create();

        using (IpcTelemetry.Context.Use(new(TraceParentA)))
        {
            (await pair.Proxy.Probe()).TraceParent.ShouldBe(TraceParentA);
            using (IpcTelemetry.Context.Use(new(TraceParentB)))
            {
                (await pair.Proxy.Probe()).TraceParent.ShouldBe(TraceParentB);
            }
            (await pair.Proxy.Probe()).TraceParent.ShouldBe(TraceParentA);
        }
        (await pair.Proxy.Probe()).TraceParent.ShouldBeNull();
    }

    [Fact]
    public void OverrideScope_DisposedTwice_RestoresOnce()
    {
        var outer = IpcTelemetry.Context.Use(new(TraceParentA));
        var inner = IpcTelemetry.Context.Use(new(TraceParentB));
        inner.Dispose();
        inner.Dispose();
        IpcTelemetry.Context.Current!.TraceParent.ShouldBe(TraceParentA);
        outer.Dispose();
        IpcTelemetry.Context.Current.ShouldBeNull();
    }

    [Fact]
    public async Task ServerPropagationNone_IgnoresHeaders()
    {
        await using var pair = await Pair.Create(serverPropagation: TelemetryPropagationPolicy.None);

        using (IpcTelemetry.Context.Use(new(TraceParentA)))
        {
            var result = await pair.Proxy.Probe();

            result.TraceParent.ShouldBeNull();
            result.ActivityTraceId.ShouldBeNull();
        }
    }

    [Fact]
    public async Task IncomingHeaderNames_AreCaseInsensitive()
    {
        await using var pair = await Pair.Create();
        using var raw = await RawConnection.Create(pair.PipeName);

        var result = await raw.Probe(new() { ["TraceParent"] = TraceParentA, ["TRACESTATE"] = TraceState, ["Baggage"] = "k=v" });

        result.TraceParent.ShouldBe(TraceParentA);
        result.TraceState.ShouldBe(TraceState);
        result.Baggage.ShouldBe("k=v");
    }

    [Fact]
    public async Task IncomingWithoutTraceParent_IsIgnored()
    {
        await using var pair = await Pair.Create();
        using var raw = await RawConnection.Create(pair.PipeName);

        var result = await raw.Probe(new() { ["tracestate"] = TraceState, ["future"] = "x" });

        result.TraceParent.ShouldBeNull();
        result.ActivityTraceId.ShouldBeNull();
    }

    [Theory]
    [InlineData("tracestate", "too-many")]
    [InlineData("baggage", "too-many")]
    [InlineData("baggage", "too-long")]
    public async Task IncomingHeadersOverTheLimits_AreAcceptedAsIs(string header, string value)
    {
        await using var pair = await Pair.Create();
        using var raw = await RawConnection.Create(pair.PipeName);
        var overLimit = value is "too-many" ? Members(header is "baggage" ? 65 : 33) : "k=" + new string('v', 8192);

        var result = await raw.Probe(new() { ["traceparent"] = TraceParentA, [header] = overLimit });

        (header is "baggage" ? result.Baggage : result.TraceState).ShouldBe(overLimit);
    }

    [Fact]
    public async Task IncomingHeaderValues_AreKeptVerbatim()
    {
        await using var pair = await Pair.Create();
        using var raw = await RawConnection.Create(pair.PipeName);

        var result = await raw.Probe(new() { ["traceparent"] = TraceParentA, ["tracestate"] = "2020-01-01T00:00:00Z" });

        result.TraceState.ShouldBe("2020-01-01T00:00:00Z");
    }

    [Fact]
    public async Task IncomingBaggage_KeepsDuplicatesInWireOrder_AndBadEscapesStayEscaped()
    {
        await using var pair = await Pair.Create();
        using var raw = await RawConnection.Create(pair.PipeName);

        var result = await raw.Probe(new() { ["traceparent"] = TraceParentA, ["baggage"] = "k=1,k=2,x=a%20b%FF" });

        result.ActivityBaggage.ShouldBe(["k=1", "k=2", "x=a b%FF"]);
    }

    [Fact]
    public async Task Baggage_WithEncodedSeparators_RoundTrips()
    {
        await using var pair = await Pair.Create();
        using var activity = StartW3CActivity();
        activity.AddBaggage("k;1", "a,b=c;d");

        var result = await pair.Proxy.Probe();

        result.Baggage.ShouldBe("k%3B1=a%2Cb%3Dc%3Bd");
        result.ActivityBaggage.ShouldBe(["k;1=a,b=c;d"]);
    }

    [Fact]
    public async Task Baggage_ReachesTheRemoteActivityAsTheLocalOneListsIt()
    {
        await using var pair = await Pair.Create();
        using var parent = StartW3CActivity();
        parent.AddBaggage("shared", "parent");
        parent.AddBaggage("inherited", "parent");
        using var child = StartW3CActivity();
        child.AddBaggage("shared", "child");

        var result = await pair.Proxy.Probe();

        result.ActivityBaggage.ShouldBe(["shared=child", "inherited=parent", "shared=parent"]);
    }

    [Theory]
    [InlineData(TelemetryPropagationPolicy.None)]
    [InlineData(TelemetryPropagationPolicy.Both)]
    public async Task UploadHandler_DoesNotSeeTheActivityOfWhoStartedTheServer(TelemetryPropagationPolicy serverPropagation)
    {
        Pair pair;
        using (StartW3CActivity())
        {
            pair = await Pair.Create(TelemetryPropagationPolicy.None, serverPropagation);
        }
        await using var _ = pair;

        (await pair.Proxy.ProbeUpload(new MemoryStream())).ActivityTraceId.ShouldBeNull();
    }

    [Theory]
    [InlineData(TelemetryPropagationPolicy.None)]
    [InlineData(TelemetryPropagationPolicy.Both)]
    public async Task UploadCallback_DoesNotSeeTheActivityOfWhoOpenedTheConnection(TelemetryPropagationPolicy clientPropagation)
    {
        await using var pair = await Pair.Create(clientPropagation, TelemetryPropagationPolicy.None);
        using (var activity = StartW3CActivity())
        {
            activity.AddBaggage("user", "alice");
            await pair.Proxy.Probe();
        }

        (await pair.Proxy.ProbeUploadViaCallback()).ActivityTraceId.ShouldBeNull();
    }

    [Fact]
    public async Task UploadHandler_DoesNotSeeTheOverrideOfWhoStartedTheServer()
    {
        Pair pair;
        using (IpcTelemetry.Context.Use(new(TraceParentA)))
        {
            pair = await Pair.Create();
        }
        await using var _ = pair;

        (await pair.Proxy.ProbeUpload(new MemoryStream())).OverrideTraceParent.ShouldBeNull();
    }

    [Fact]
    public async Task UploadCallback_DoesNotSeeTheOverrideOfWhoOpenedTheConnection()
    {
        await using var pair = await Pair.Create();
        using (IpcTelemetry.Context.Use(new(TraceParentA)))
        {
            await pair.Proxy.Probe();
        }

        (await pair.Proxy.ProbeUploadViaCallback()).OverrideTraceParent.ShouldBeNull();
    }

    [Fact]
    public async Task AmbientTraceStateOverTheMemberLimit_IsReceivedAsIs()
    {
        await using var pair = await Pair.Create();
        using var activity = StartW3CActivity();
        activity.TraceStateString = Members(40);

        (await pair.Proxy.Probe()).TraceState.ShouldBe(Members(40));
    }

    [Fact]
    public async Task AmbientBaggageOverTheMemberLimit_IsReceivedAsIs()
    {
        await using var pair = await Pair.Create();
        using var activity = StartW3CActivity();
        for (var index = 0; index < 70; index++)
        {
            activity.AddBaggage($"k{index}", "v");
        }

        var result = await pair.Proxy.Probe();

        result.Baggage.ShouldBe(activity.ToBaggageHeader());
        result.ActivityBaggage!.Length.ShouldBe(70);
    }

    [Fact]
    public async Task AmbientBaggageOverTheByteLimit_IsReceivedAsIs()
    {
        await using var pair = await Pair.Create();
        using var activity = StartW3CActivity();
        activity.AddBaggage("big", new string('v', 9_000));

        (await pair.Proxy.Probe()).Baggage.ShouldBe("big=" + new string('v', 9_000));
    }

    [Fact]
    public async Task Callback_PropagatesTheServerActivity()
    {
        await using var pair = await Pair.Create();
        using var activity = StartW3CActivity();
        activity.AddBaggage("k", "v");

        var result = await pair.Proxy.ProbeViaCallback();

        result.Server.ActivityTraceId.ShouldBe(activity.TraceId.ToHexString());
        result.Callback.ActivityTraceId.ShouldBe(activity.TraceId.ToHexString());
        var serverClientSpanId = result.Callback.TraceParent!.Substring(36, 16);
        result.Callback.TraceParent.ShouldBe($"00-{activity.TraceId.ToHexString()}-{serverClientSpanId}-00");
        serverClientSpanId.ShouldNotBe(result.Server.ActivitySpanId);
        result.Callback.ActivityParentSpanId.ShouldBe(serverClientSpanId);
        result.Callback.Baggage.ShouldBe("k=v");
    }

    [Fact]
    public async Task Callback_HonorsAnOverrideSetByTheServer()
    {
        await using var pair = await Pair.Create();

        var result = await pair.Proxy.ProbeViaCallback(TraceParentB);

        result.Callback.TraceParent.ShouldBe(TraceParentB);
    }

    [Fact]
    public async Task Callback_ClientPropagationNone_IgnoresCallbackHeaders()
    {
        await using var pair = await Pair.Create(clientPropagation: TelemetryPropagationPolicy.None);

        var result = await pair.Proxy.ProbeViaCallback(TraceParentB);

        result.Callback.TraceParent.ShouldBeNull();
    }

    [Fact]
    public async Task ClientOutgoingOnly_SendsCalls_AndIgnoresCallbackHeaders()
    {
        await using var pair = await Pair.Create(clientPropagation: TelemetryPropagationPolicy.Outgoing);

        using (IpcTelemetry.Context.Use(new(TraceParentA)))
        {
            var result = await pair.Proxy.ProbeViaCallback(TraceParentB);

            result.Server.TraceParent.ShouldBe(TraceParentA);
            result.Callback.TraceParent.ShouldBeNull();
            result.Callback.ActivityTraceId.ShouldBeNull();
        }
    }

    [Fact]
    public async Task ClientIncomingOnly_SendsNothing_AndReadsCallbackHeaders()
    {
        await using var pair = await Pair.Create(clientPropagation: TelemetryPropagationPolicy.Incoming);

        using (IpcTelemetry.Context.Use(new(TraceParentA)))
        {
            var result = await pair.Proxy.ProbeViaCallback(TraceParentB);

            result.Server.TraceParent.ShouldBeNull();
            result.Server.ActivityTraceId.ShouldBeNull();
            result.Callback.TraceParent.ShouldBe(TraceParentB);
            result.Callback.ActivityParentSpanId.ShouldBe("b7ad6b7169203331");
        }
    }

    [Fact]
    public async Task ServerIncomingOnly_ReadsCalls_AndSendsNothingWithCallbacks()
    {
        await using var pair = await Pair.Create(serverPropagation: TelemetryPropagationPolicy.Incoming);

        using (IpcTelemetry.Context.Use(new(TraceParentA)))
        {
            var result = await pair.Proxy.ProbeViaCallback();

            result.Server.TraceParent.ShouldBe(TraceParentA);
            result.Server.ActivityParentSpanId.ShouldBe("00f067aa0ba902b7");
            result.Callback.TraceParent.ShouldBeNull();
            result.Callback.ActivityTraceId.ShouldBeNull();
        }
    }

    [Fact]
    public async Task ServerOutgoingOnly_IgnoresCallHeaders_AndSendsCallbacks()
    {
        await using var pair = await Pair.Create(serverPropagation: TelemetryPropagationPolicy.Outgoing);

        using (IpcTelemetry.Context.Use(new(TraceParentA)))
        {
            var result = await pair.Proxy.ProbeViaCallback(TraceParentB);

            result.Server.TraceParent.ShouldBeNull();
            result.Server.ActivityTraceId.ShouldBeNull();
            result.Callback.TraceParent.ShouldBe(TraceParentB);
        }
    }

    [Fact]
    public async Task ServerActivity_ComesFromTheIpcActivitySource_WhenListened()
    {
        var started = new List<Activity>();
        var baggageAtStart = new List<string>();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "UiPath.Ipc",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity =>
            {
                lock (started)
                {
                    started.Add(activity);
                    baggageAtStart.AddRange(activity.Baggage.Select(pair => $"{pair.Key}={pair.Value}"));
                }
            },
            ActivityStopped = activity => { lock (stopped) stopped.Add(activity); },
        };
        ActivitySource.AddActivityListener(listener);
        await using var pair = await Pair.Create();

        using (IpcTelemetry.Context.Use(new(TraceParentA, TraceState, "k=v")))
        {
            var result = await pair.Proxy.Probe();
            result.ActivityParentSpanId.ShouldBe("00f067aa0ba902b7");
            result.ActivityBaggage.ShouldBe(["k=v"]);
        }

        var server = started.ShouldHaveSingleItem();
        server.Kind.ShouldBe(ActivityKind.Server);
        server.DisplayName.ShouldBe($"{nameof(ITraceProbe)}/{nameof(ITraceProbe.Probe)}");
        server.TraceId.ToHexString().ShouldBe("4bf92f3577b34da6a3ce929d0e0e4736");
        server.TraceStateString.ShouldBe(TraceState);
        server.HasRemoteParent.ShouldBeTrue();
        baggageAtStart.ShouldBe(["k=v"]);
        stopped.ShouldHaveSingleItem().ShouldBeSameAs(server);
    }

    [Fact]
    public async Task ClientActivity_ComesFromTheIpcActivitySource_WhenListened()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "UiPath.Ipc",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => { lock (stopped) stopped.Add(activity); },
        };
        ActivitySource.AddActivityListener(listener);
        await using var pair = await Pair.Create();
        using var caller = StartW3CActivity();

        var result = await pair.Proxy.Probe();

        var client = stopped.Where(activity => activity.ParentSpanId == caller.SpanId).ShouldHaveSingleItem();
        client.Kind.ShouldBe(ActivityKind.Client);
        client.DisplayName.ShouldBe($"{nameof(ITraceProbe)}/{nameof(ITraceProbe.Probe)}");
        result.TraceParent.ShouldBe(client.Id);
        result.ActivityParentSpanId.ShouldBe(client.SpanId.ToHexString());
        Activity.Current.ShouldBeSameAs(caller);
    }

    [Fact]
    public async Task ClientIncomingOnly_StartsNoClientActivity()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "UiPath.Ipc",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => { lock (stopped) stopped.Add(activity); },
        };
        ActivitySource.AddActivityListener(listener);
        await using var pair = await Pair.Create(clientPropagation: TelemetryPropagationPolicy.Incoming);
        using var caller = StartW3CActivity();

        var result = await pair.Proxy.Probe();

        result.TraceParent.ShouldBeNull();
        stopped.ShouldNotContain(activity => activity.ParentSpanId == caller.SpanId);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("")]
    public void Use_InvalidTraceParent_Throws(string traceParent)
    {
        var ex = Should.Throw<ArgumentException>(() => IpcTelemetry.Context.Use(new(traceParent)));
        ex.ParamName.ShouldBe("traceContext");
        ex.Message.ShouldContain("traceparent");
    }

    [Fact]
    public void Use_TooManyTraceStateMembers_Throws()
    {
        IpcTelemetry.Context.Use(new(TraceParentA, Members(32))).Dispose();
        var ex = Should.Throw<ArgumentException>(() => IpcTelemetry.Context.Use(new(TraceParentA, Members(33))));
        ex.ParamName.ShouldBe("traceContext");
        ex.Message.ShouldContain("tracestate");
    }

    [Fact]
    public void Use_BaggageLimits_Throw()
    {
        IpcTelemetry.Context.Use(new(TraceParentA, baggage: Members(64))).Dispose();
        IpcTelemetry.Context.Use(new(TraceParentA, baggage: "k=" + new string('v', 8190))).Dispose();
        ShouldReject(Members(65));
        ShouldReject("k=" + new string('v', 8191));
        // UTF-8 bytes, not chars.
        ShouldReject("k=" + new string('é', 4096));

        static void ShouldReject(string baggage)
        {
            var ex = Should.Throw<ArgumentException>(() => IpcTelemetry.Context.Use(new(TraceParentA, baggage: baggage)));
            ex.ParamName.ShouldBe("traceContext");
            ex.Message.ShouldContain("baggage");
        }
    }

    [Fact]
    public void Use_Null_Throws()
        => Should.Throw<ArgumentNullException>(() => IpcTelemetry.Context.Use(null!));

    [Fact]
    public void ToString_DoesNotPrintBaggage()
        => new IpcTelemetry.Context(TraceParentA, TraceState, "secret=1").ToString().ShouldNotContain("secret");

    [Fact]
    public void RequestWithoutHeaders_HasNoHeadersOnTheWire()
        => IpcJsonSerializer.Instance.Serialize(new Request("ISystemService", "3", "Foo", ["1"], 0))
            .ShouldBe("""{"Endpoint":"ISystemService","Id":"3","MethodName":"Foo","Parameters":["1"],"TimeoutInSeconds":0.0}""");

    [Fact]
    public void RequestWithHeaders_WritesThemVerbatim_AndRoundTrips()
    {
        var request = new Request("ISystemService", "3", "Foo", ["1"], 0)
        {
            Headers = new() { ["traceparent"] = TraceParentA, ["baggage"] = "userId=alice" },
        };

        var json = IpcJsonSerializer.Instance.Serialize(request);

        json.ShouldBe("""{"Endpoint":"ISystemService","Id":"3","MethodName":"Foo","Parameters":["1"],"TimeoutInSeconds":0.0,"Headers":{"traceparent":""" + $"\"{TraceParentA}\"" + ""","baggage":"userId=alice"}}""");
        var roundTripped = (Request)IpcJsonSerializer.Instance.Deserialize(json, typeof(Request))!;
        roundTripped.Headers.ShouldBe(request.Headers);
        roundTripped.ToString().ShouldBe("ISystemService Foo 3.");
    }

    [Fact]
    public void ToBaggageHeader_IsNull_WithoutBaggage()
    {
        using var activity = StartW3CActivity();

        activity.ToBaggageHeader().ShouldBeNull();
    }

    [Fact]
    public void ToBaggageHeader_PercentEncodesKeysAndValues()
    {
        using var activity = StartW3CActivity();
        activity.AddBaggage("user id", "a,b=c;d");
        activity.AddBaggage("empty", null);

        activity.ToBaggageHeader().ShouldBe("empty=,user%20id=a%2Cb%3Dc%3Bd");
    }

    [Fact]
    public void ToBaggageHeader_ListsTheBaggageAsActivityDoes()
    {
        using var parent = StartW3CActivity();
        parent.AddBaggage("shared", "parent");
        parent.AddBaggage("inherited", "parent");
        using var child = StartW3CActivity();
        child.AddBaggage("shared", "child");
        child.AddBaggage("shared", "child-newer");

        child.ToBaggageHeader().ShouldBe("shared=child-newer,shared=child,inherited=parent,shared=parent");
    }

    private static Activity StartW3CActivity() => new Activity("caller").SetIdFormat(ActivityIdFormat.W3C).Start();

    private static string Members(int count) => string.Join(",", Enumerable.Range(0, count).Select(i => $"k{i}=v"));

    private sealed class RawConnection : IDisposable
    {
        private readonly NamedPipeClientStream _pipe;
        private int _requestId;

        private RawConnection(NamedPipeClientStream pipe) => _pipe = pipe;

        public static async Task<RawConnection> Create(string pipeName)
        {
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(5_000);
            return new(pipe);
        }

        // Frames are written by hand so the headers can break the rules the client enforces.
        public async Task<TraceProbeResult> Probe(Dictionary<string, string> headers)
        {
            var json = $$"""{"Endpoint":"{{nameof(ITraceProbe)}}","Id":"{{++_requestId}}","MethodName":"{{nameof(ITraceProbe.Probe)}}","Parameters":[],"TimeoutInSeconds":0,"Headers":{{Newtonsoft.Json.JsonConvert.SerializeObject(headers)}}}""";
            var payload = Encoding.UTF8.GetBytes(json);
            var frame = new byte[5 + payload.Length];
            frame[0] = (byte)MessageType.Request;
            BitConverter.GetBytes(payload.Length).CopyTo(frame, 1);
            payload.CopyTo(frame, 5);
            await _pipe.WriteAsync(frame, 0, frame.Length);

            var header = await Read(5);
            ((MessageType)header[0]).ShouldBe(MessageType.Response);
            var body = await Read(BitConverter.ToInt32(header, 1));
            var response = (Response)IpcJsonSerializer.Instance.Deserialize(Encoding.UTF8.GetString(body), typeof(Response))!;
            return response.Deserialize<TraceProbeResult>();
        }

        private async Task<byte[]> Read(int length)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var buffer = new byte[length];
            for (var offset = 0; offset < length;)
            {
                var read = await _pipe.ReadAsync(buffer, offset, length - offset, timeout.Token);
                if (read is 0)
                {
                    throw new IOException("Connection closed.");
                }
                offset += read;
            }
            return buffer;
        }

        public void Dispose() => _pipe.Dispose();
    }

    private sealed class Pair : IAsyncDisposable
    {
        private readonly IpcServer _server;
        public ITraceProbe Proxy { get; }
        public string PipeName { get; }

        private Pair(IpcServer server, ITraceProbe proxy, string pipeName)
        {
            _server = server;
            Proxy = proxy;
            PipeName = pipeName;
        }

        public static async Task<Pair> Create(
            TelemetryPropagationPolicy clientPropagation = TelemetryPropagationPolicy.Both,
            TelemetryPropagationPolicy serverPropagation = TelemetryPropagationPolicy.Both)
        {
            var pipeName = $"ipctest_trace_{Guid.NewGuid():N}";

            var server = new IpcServer
            {
                Transport = new NamedPipeServerTransport { PipeName = pipeName },
                Endpoints = new() { { typeof(ITraceProbe), new TraceProbe() } },
                TelemetryPropagation = serverPropagation,
            };

            var client = new IpcClient
            {
                Transport = new NamedPipeClientTransport { PipeName = pipeName },
                Callbacks = new() { { typeof(ITraceProbeCallback), new TraceProbeCallback() } },
                TelemetryPropagation = clientPropagation,
                RequestTimeout = TimeSpan.FromSeconds(10),
            };
            var proxy = client.GetProxy<ITraceProbe>();

            server.Start();
            await Task.Yield();
            return new Pair(server, proxy, pipeName);
        }

        public async ValueTask DisposeAsync()
        {
            (Proxy as IpcProxy)?.Dispose();
            await ((Proxy as IpcProxy)?.CloseConnection() ?? default);
            await _server.DisposeAsync();
        }
    }
}
