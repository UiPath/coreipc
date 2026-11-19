using System.IO.Pipes;
using System.Text;
using UiPath.Ipc.Transport.NamedPipe;

namespace UiPath.Ipc.Tests;

public sealed record TraceProbeResult(
    string? ActivityTraceId,
    string? ActivityParentSpanId,
    string? ActivitySpanId,
    string? ActivityTraceState,
    string[]? ActivityBaggage,
    int SchedulerId)
{
    public static TraceProbeResult Capture()
    {
        var activity = Activity.Current;
        return new(
            activity?.TraceId.ToHexString(),
            activity?.ParentSpanId.ToHexString(),
            activity?.SpanId.ToHexString(),
            activity?.TraceStateString,
            activity?.Baggage.Select(pair => $"{pair.Key}={pair.Value}").ToArray(),
            TaskScheduler.Current.Id);
    }
}

public sealed record TraceProbeCallbackResult(TraceProbeResult Server, TraceProbeResult Callback);

public interface ITraceProbe
{
    Task<TraceProbeResult> Probe();
    Task<TraceProbeCallbackResult> ProbeViaCallback(string? traceParent = null);
    Task<TraceProbeResult> ProbeUpload(Stream stream);
    Task<TraceProbeResult> ProbeUploadViaCallback();
    Task<TraceProbeResult> ProbeViaSink(string sinkPipeName);
    Task<TraceProbeResult> Fail();
    Task FailOneWay();
    // Not a plain Task: the server answers a call to one of those at once, without waiting for the handler.
    Task<TraceProbeResult> Hang(CancellationToken ct = default);
}

public interface ITraceProbeCallback
{
    Task<TraceProbeResult> Probe();
    Task<TraceProbeResult> ProbeUpload(Stream stream);
}

public sealed class TraceProbe : ITraceProbe
{
    public Task<TraceProbeResult> Probe() => Task.FromResult(TraceProbeResult.Capture());

    /// <param name="traceParent">When given, the callback is made under a W3C activity with this parent, whatever the call carried.</param>
    public async Task<TraceProbeCallbackResult> ProbeViaCallback(string? traceParent = null)
    {
        var server = TraceProbeResult.Capture();
        using var activity = traceParent is null ? null : new Activity("handler").SetParentId(traceParent).Start();
        return new(server, await IpcContext.Current!.GetCallback<ITraceProbeCallback>().Probe());
    }

    public Task<TraceProbeResult> ProbeUpload(Stream stream) => Task.FromResult(TraceProbeResult.Capture());

    public Task<TraceProbeResult> ProbeUploadViaCallback()
    => IpcContext.Current!.GetCallback<ITraceProbeCallback>().ProbeUpload(new MemoryStream());

    public async Task<TraceProbeResult> ProbeViaSink(string sinkPipeName)
    {
        var sink = new IpcClient
        {
            Transport = new NamedPipeClientTransport { PipeName = sinkPipeName },
            TelemetryPropagation = TelemetryPropagationPolicy.Outgoing,
            RequestTimeout = TimeSpan.FromSeconds(10),
        }.GetProxy<ITraceProbe>();
        using (sink as IpcProxy)
        {
            return await sink.Probe();
        }
    }

    public Task<TraceProbeResult> Fail() => throw new InvalidOperationException("probe failed");

    public Task FailOneWay() => throw new InvalidOperationException("one-way probe failed");

    public async Task<TraceProbeResult> Hang(CancellationToken ct = default)
    {
        await Task.Delay(Timeout.Infinite, ct);
        return TraceProbeResult.Capture();
    }
}

public sealed class TraceProbeCallback : ITraceProbeCallback
{
    public Task<TraceProbeResult> Probe() => Task.FromResult(TraceProbeResult.Capture());

    public Task<TraceProbeResult> ProbeUpload(Stream stream) => Task.FromResult(TraceProbeResult.Capture());
}

public sealed class TraceContextTests
{
    private const string TraceParentA = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
    private const string TraceIdA = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string SpanIdA = "00f067aa0ba902b7";
    private const string TraceParentB = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-00";
    private const string TraceIdB = "0af7651916cd43dd8448eb211c80319c";
    private const string TraceState = "congo=t61rcWkgMzE,rojo=00f067aa0ba902b7";
    // U+FFFD, what a value that isn't valid UTF-8 decodes to.
    private static readonly string Replacement = char.ConvertFromUtf32(0xFFFD);

    [Fact]
    public async Task ClientPropagationNone_SendsNothing()
    {
        await using var pair = await Pair.Create(clientPropagation: TelemetryPropagationPolicy.None);
        using var activity = StartW3CActivity();

        (await pair.Proxy.Probe()).ActivityTraceId.ShouldBeNull();
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

        var sent = SentHeaders()!;
        var result = await pair.Proxy.Probe();

        sent.Keys.ShouldBe(["traceparent", "tracestate", "baggage"]);
        sent["traceparent"]!.ShouldMatch($"^00-{activity.TraceId.ToHexString()}-[0-9a-f]{{16}}-01$");
        sent["traceparent"]!.Substring(36, 16).ShouldNotBe(activity.SpanId.ToHexString());
        sent["tracestate"].ShouldBe(TraceState);
        sent["baggage"].ShouldBe("tenant = a%2Cb, user = alice%20smith");
        result.ActivityTraceId.ShouldBe(activity.TraceId.ToHexString());
        result.ActivityParentSpanId.ShouldNotBe(activity.SpanId.ToHexString());
        result.ActivitySpanId.ShouldNotBe(result.ActivityParentSpanId);
        result.ActivityTraceState.ShouldBe(TraceState);
        result.ActivityBaggage.ShouldBe(["tenant=a,b", "user=alice smith"]);
    }

    [Fact]
    public async Task HierarchicalActivity_IsNotPropagated()
    {
        await using var pair = await Pair.Create();
        using var activity = new Activity("caller").SetIdFormat(ActivityIdFormat.Hierarchical).Start();

        (await pair.Proxy.Probe()).ActivityTraceId.ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForcedHierarchicalDefault_StillPropagatesAW3CActivity(bool sampled)
    {
        await using var pair = await Pair.Create();
        using var listener = sampled ? ListenToIpcSpans(out _) : null;
        using var activity = StartW3CActivity();
        var (format, force) = (Activity.DefaultIdFormat, Activity.ForceDefaultIdFormat);
        try
        {
            Activity.DefaultIdFormat = ActivityIdFormat.Hierarchical;
            Activity.ForceDefaultIdFormat = true;

            SentHeaders()!["traceparent"].ShouldMatch($"^00-{activity.TraceId.ToHexString()}-[0-9a-f]{{16}}-0[01]$");
            (await pair.Proxy.Probe()).ActivityTraceId.ShouldBe(activity.TraceId.ToHexString());
        }
        finally
        {
            (Activity.DefaultIdFormat, Activity.ForceDefaultIdFormat) = (format, force);
        }
    }

    [Fact]
    public async Task NoAmbientContext_SendsNothing()
    {
        await using var pair = await Pair.Create();

        (await pair.Proxy.Probe()).ActivityTraceId.ShouldBeNull();
    }

    [Fact]
    public async Task ServerPropagationNone_IgnoresHeaders()
    {
        await using var pair = await Pair.Create(serverPropagation: TelemetryPropagationPolicy.None);
        using var activity = StartW3CActivity();

        (await pair.Proxy.Probe()).ActivityTraceId.ShouldBeNull();
    }

    [Fact]
    public async Task IncomingHeaderNames_AreReadOnlyInLowercase()
    {
        await using var pair = await Pair.Create();
        using var raw = await RawConnection.Create(pair.PipeName);

        var result = await raw.Probe(new() { ["TraceParent"] = TraceParentA, ["TRACESTATE"] = TraceState, ["Baggage"] = "k=v" });

        result.ActivityTraceId.ShouldBeNull();
    }

    [Fact]
    public async Task IncomingCorrelationContext_IsNotReadAsBaggage()
    {
        await using var pair = await Pair.Create();
        using var raw = await RawConnection.Create(pair.PipeName);

        var result = await raw.Probe(new() { ["traceparent"] = TraceParentA, ["Correlation-Context"] = "k=v" });

        result.ActivityTraceId.ShouldBe(TraceIdA);
        result.ActivityBaggage.ShouldBeEmpty();
    }

    [Fact]
    public async Task IncomingWithoutTraceParent_IsIgnored()
    {
        await using var pair = await Pair.Create();
        using var raw = await RawConnection.Create(pair.PipeName);

        var result = await raw.Probe(new() { ["tracestate"] = TraceState, ["future"] = "x" });

        result.ActivityTraceId.ShouldBeNull();
    }

    [Fact]
    public async Task IncomingBaggage_HasNoMemberOrLengthLimit()
    {
        await using var pair = await Pair.Create();
        using var raw = await RawConnection.Create(pair.PipeName);

        var result = await raw.Probe(new() { ["traceparent"] = TraceParentA, ["baggage"] = Members(65) + ",big=" + new string('v', 8192) });

        result.ActivityBaggage!.Length.ShouldBe(66);
    }

    [Fact]
    public async Task IncomingTraceState_HasNoMemberLimit_AndIsCutAt256Characters()
    {
        await using var pair = await Pair.Create();
        using var raw = await RawConnection.Create(pair.PipeName);

        var result = await raw.Probe(new() { ["traceparent"] = TraceParentA, ["tracestate"] = Members(60) });

        // 44 members take 254 characters with their commas; the next would take it to 260.
        result.ActivityTraceState.ShouldBe(Members(44));
    }

    // Each row has a name: xUnit cuts the arguments it shows to 50 characters, so many rows would look the same.
    [Theory]
    [InlineData("values-not-strings", """{"traceparent":5,"tracestate":{"a":1},"baggage":["k=v"]}""", null)]
    [InlineData("string-not-object", "\"not an object\"", null)]
    [InlineData("array-not-object", "[1,2]", null)]
    // A whole value that looks like a date, too big for one, is read as text, so it is skipped like any string.
    [InlineData("date-not-object", "\"/Date(300000000000000)/\"", null)]
    // Skipped without reading its dates, which would overflow.
    [InlineData("array-with-a-date", """["/Date(300000000000000)/"]""", null)]
    // Only the value that isn't a string is lost.
    [InlineData("tracestate-number", $$"""{"traceparent":"{{TraceParentA}}","tracestate":5}""", TraceIdA)]
    // Only the lowercase name is read, and of a repeated name the last one counts, even when it isn't a string.
    [InlineData("null-exact-name", $$"""{"traceparent":null,"TraceParent":"{{TraceParentA}}"}""", null)]
    [InlineData("null-exact-name-last", $$"""{"TraceParent":"{{TraceParentA}}","traceparent":null}""", null)]
    [InlineData("other-case-only", $$"""{"TraceParent":"{{TraceParentA}}"}""", null)]
    [InlineData("repeated-name", $$"""{"traceparent":"{{TraceParentA}}","traceparent":5}""", null)]
    // A repeated Headers (the helper closes the request after it): the last one counts, even when it isn't an object.
    [InlineData("repeated-headers-number", $$"""{"traceparent":"{{TraceParentA}}"},"Headers":5""", null)]
    [InlineData("repeated-headers-null", $$"""{"traceparent":"{{TraceParentA}}"},"Headers":null""", null)]
    // Another case of the name, which JS and Python ignore, neither reads nor overrides Headers, even with a date inside.
    [InlineData("lowercase-headers-not-read", $$"""{},"headers":{"traceparent":"{{TraceParentA}}"}""", null)]
    [InlineData("lowercase-headers-not-overriding", $$"""{"traceparent":"{{TraceParentA}}"},"headers":{}""", TraceIdA)]
    [InlineData("uppercase-headers-not-overriding", $$"""{"traceparent":"{{TraceParentA}}"},"HEADERS":5""", TraceIdA)]
    [InlineData("lowercase-headers-with-a-date", $$"""{"traceparent":"{{TraceParentA}}"},"headers":{"k":"/Date(300000000000000)/"}""", TraceIdA)]
    public async Task IncomingHeadersThatBreakTheFormat_AreReadLikeJsAndPython_AndTheConnectionStaysUp(string row, string headers, string? traceId)
    {
        await using var pair = await Pair.Create();
        using var raw = await RawConnection.Create(pair.PipeName);

        var received = await raw.ProbeWithHeadersJson(headers);
        var next = await raw.Probe(new() { ["traceparent"] = TraceParentA });

        received.ActivityTraceId.ShouldBe(traceId, row);
        next.ActivityTraceId.ShouldBe(TraceIdA, row);
    }

    [Fact]
    public async Task IncomingBaggageWithNothingUsable_GivesNoActivity()
    {
        await using var pair = await Pair.Create();
        using var raw = await RawConnection.Create(pair.PipeName);

        // The reading stops at the first member, which has no '='.
        var result = await raw.Probe(new() { ["baggage"] = "foo,a=1" });

        result.ActivityTraceId.ShouldBeNull();
    }

    [Theory]
    // The raw text up to the end of the last valid member, spaces around the members kept.
    [InlineData("congo=t61rcWkgMzE, rojo=00f067aa0ba902b7 ,Bad=1,x=y", "congo=t61rcWkgMzE, rojo=00f067aa0ba902b7 ")]
    [InlineData("congo=t61rcWkgMzE,", "congo=t61rcWkgMzE")]
    [InlineData("Bad=1,congo=t61rcWkgMzE", null)]
    [InlineData("2020-01-01T00:00:00Z,congo=t61rcWkgMzE", null)]
    [InlineData("a=x y", null)]
    [InlineData("a=", null)]
    public async Task IncomingTraceState_IsCutAtTheFirstMemberThatBreaksTheGrammar(string traceState, string? kept)
    {
        await using var pair = await Pair.Create();
        using var raw = await RawConnection.Create(pair.PipeName);

        var result = await raw.Probe(new() { ["traceparent"] = TraceParentA, ["tracestate"] = traceState });

        result.ActivityTraceId.ShouldBe(TraceIdA);
        result.ActivityTraceState.ShouldBe(kept);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("00-4BF92F3577B34DA6A3CE929D0E0E4736-00f067aa0ba902b7-01")]
    [InlineData("00-00000000000000000000000000000000-00f067aa0ba902b7-01")]
    [InlineData("00-4bf92f3577b34da6a3ce929d0e0e4736-0000000000000000-01")]
    [InlineData("ff-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01")]
    [InlineData("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01-extra")]
    [InlineData("01-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01extra")]
    [InlineData("01-4bf92f3577b34da6a3ce929d0e0e4736_00f067aa0ba902b7-01")]
    public async Task IncomingInvalidTraceParent_IsIgnored_WithItsTraceState(string traceParent)
    {
        await using var pair = await Pair.Create();
        using var raw = await RawConnection.Create(pair.PipeName);

        var result = await raw.Probe(new() { ["traceparent"] = traceParent, ["tracestate"] = TraceState });
        var withBaggage = await raw.Probe(new() { ["traceparent"] = traceParent, ["tracestate"] = TraceState, ["baggage"] = "k=v" });

        result.ActivityTraceId.ShouldBeNull();
        withBaggage.ActivityTraceId.ShouldNotBe(TraceIdA);
        withBaggage.ActivityTraceState.ShouldBeNull();
        withBaggage.ActivityBaggage.ShouldBe(["k=v"]);
    }

    [Theory]
    [InlineData("01-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01")]
    [InlineData("01-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01-future")]
    [InlineData("01-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01-é")]
    public async Task IncomingHigherVersionTraceParent_IsReadByItsFirst55Characters(string traceParent)
    {
        await using var pair = await Pair.Create();
        using var raw = await RawConnection.Create(pair.PipeName);

        var result = await raw.Probe(new() { ["traceparent"] = traceParent, ["tracestate"] = TraceState });

        result.ActivityTraceId.ShouldBe(TraceIdA);
        result.ActivityParentSpanId.ShouldBe(SpanIdA);
        result.ActivityTraceState.ShouldBe(TraceState);
    }

    [Fact]
    public async Task IncomingBaggageWithoutTraceParent_IsReceived()
    {
        await using var pair = await Pair.Create();
        using var raw = await RawConnection.Create(pair.PipeName);

        var result = await raw.Probe(new() { ["baggage"] = "k=v" });

        result.ActivityBaggage.ShouldBe(["k=v"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncomingBaggageWithoutTraceParent_IsPassedOnUnderANewW3CTrace(bool listened)
    {
        using var listener = listened ? ListenToIpcSpans(out _) : null;
        await using var sink = await Pair.Create();
        await using var pair = await Pair.Create();
        using var raw = await RawConnection.Create(pair.PipeName);

        var result = await raw.Call(nameof(ITraceProbe.ProbeViaSink), new() { ["baggage"] = "k=v" }, sink.PipeName);

        result.ActivityTraceId.ShouldNotBeNull();
        result.ActivityTraceId.ShouldNotBe(new string('0', 32));
        result.ActivityBaggage.ShouldBe(["k=v"]);
    }

    [Fact]
    public async Task IncomingBaggage_KeepsTheHeaderOrder_SoTheFirstOfARepeatedKeyWins()
    {
        await using var pair = await Pair.Create();
        using var raw = await RawConnection.Create(pair.PipeName);

        var result = await raw.Probe(new() { ["traceparent"] = TraceParentA, ["baggage"] = "k=1,k=2,x=a%20b%FF" });

        // Activity.GetBaggageItem reads the first.
        result.ActivityBaggage.ShouldBe(["k=1", "k=2", "x=a b" + Replacement]);
    }

    [Fact]
    public async Task IncomingBaggage_SkipsMembersItCantDecode_AndStopsAtOneWithoutAValue()
    {
        await using var pair = await Pair.Create();
        using var raw = await RawConnection.Create(pair.PipeName);

        var result = await raw.Probe(new()
        {
            ["traceparent"] = TraceParentA,
            ["baggage"] = "a=1, b = 2 ,bad key=x,c=3;ttl=30,d=%zz,e=xé,k%41=%c3%a9,f=50%,g=2,h=,i=3",
        });

        result.ActivityBaggage.ShouldBe(["a=1", "b=2", "c=3;ttl=30", "k%41=é", "g=2"]);
    }

    [Fact]
    public async Task Baggage_WithEncodedSeparators_RoundTrips()
    {
        await using var pair = await Pair.Create();
        using var activity = StartW3CActivity();
        // Not sent: a key must be a token.
        activity.AddBaggage("k;1", "x");
        activity.AddBaggage("k", "a,b=c;d");

        var sent = SentHeaders()!;
        var result = await pair.Proxy.Probe();

        sent["baggage"].ShouldBe("k = a%2Cb=c%3Bd");
        result.ActivityBaggage.ShouldBe(["k=a,b=c;d"]);
    }

    [Fact]
    public async Task Baggage_ReachesTheRemoteActivity_InActivityBaggageOrder_SoTheNewestValueWins()
    {
        await using var pair = await Pair.Create();
        using var parent = StartW3CActivity();
        parent.AddBaggage("shared", "parent");
        parent.AddBaggage("inherited", "parent");
        using var child = StartW3CActivity();
        child.AddBaggage("shared", "child");

        var sent = SentHeaders()!;
        var result = await pair.Proxy.Probe();

        sent["baggage"].ShouldBe("shared = child, inherited = parent, shared = parent");
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
    public void AmbientTraceStateOver256Characters_IsCutFromTheEnd()
    {
        using var activity = StartW3CActivity();
        activity.TraceStateString = Members(60);

        SentHeaders()!["tracestate"].ShouldBe(Members(44));
    }

    [Fact]
    public async Task AmbientBaggageOverTheMemberLimit_SendsTheNewest64()
    {
        await using var pair = await Pair.Create();
        using var activity = StartW3CActivity();
        for (var index = 0; index < 70; index++)
        {
            activity.AddBaggage($"k{index}", "v");
        }

        var sent = SentHeaders()!;
        var result = await pair.Proxy.Probe();

        sent["baggage"].ShouldBe(string.Join(", ", Enumerable.Range(6, 64).Reverse().Select(index => $"k{index} = v")));
        result.ActivityBaggage!.Length.ShouldBe(64);
    }

    [Fact]
    public async Task AmbientBaggageWithALoneSurrogate_IsSentAsU_FFFD()
    {
        await using var pair = await Pair.Create();
        using var activity = StartW3CActivity();
        activity.AddBaggage("k", "a" + (char)0xD800 + "b");

        var sent = SentHeaders()!;
        var result = await pair.Proxy.Probe();

        sent["baggage"].ShouldBe("k = a%EF%BF%BDb");
        result.ActivityBaggage.ShouldBe(["k=a" + Replacement + "b"]);
    }

    [Fact]
    public void AmbientBaggageOverTheByteLimit_StopsAtTheMemberThatDoesNotFit()
    {
        using var activity = StartW3CActivity();
        activity.AddBaggage("after", "v");
        activity.AddBaggage("big", new string('v', 70_000));
        activity.AddBaggage("small", "v");

        SentHeaders()!["baggage"].ShouldBe("small = v");
    }

    [Theory]
    // "k = " + value + ", " must stay under 8192 characters.
    [InlineData(8185, true)]
    [InlineData(8186, false)]
    public void AmbientBaggageNearTheByteLimit_IsSentOnlyWhenItFits(int valueLength, bool sent)
    {
        using var activity = StartW3CActivity();
        var value = new string('v', valueLength);
        activity.AddBaggage("k", value);

        SentHeaders()!.TryGetValue("baggage", out var baggage);
        baggage.ShouldBe(sent ? "k = " + value : null);
    }

    [Fact]
    public void AmbientBaggageWithoutAKey_IsNotSent()
    {
        using var activity = StartW3CActivity();
        activity.AddBaggage(null!, "x");
        activity.AddBaggage("", "y");
        activity.AddBaggage("k", "v");
        activity.AddBaggage("empty", null);

        SentHeaders()!["baggage"].ShouldBe("empty = , k = v");
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
        // The server's client span sits between the two.
        result.Callback.ActivityParentSpanId.ShouldNotBe(result.Server.ActivitySpanId);
        result.Callback.ActivityBaggage.ShouldBe(["k=v"]);
    }

    [Fact]
    public async Task Callback_ClientPropagationNone_IgnoresCallbackHeaders()
    {
        await using var pair = await Pair.Create(clientPropagation: TelemetryPropagationPolicy.None);

        var result = await pair.Proxy.ProbeViaCallback(TraceParentB);

        result.Callback.ActivityTraceId.ShouldBeNull();
    }

    [Fact]
    public async Task ClientOutgoingOnly_SendsCalls_AndIgnoresCallbackHeaders()
    {
        await using var pair = await Pair.Create(clientPropagation: TelemetryPropagationPolicy.Outgoing);
        using var activity = StartW3CActivity();

        var result = await pair.Proxy.ProbeViaCallback(TraceParentB);

        result.Server.ActivityTraceId.ShouldBe(activity.TraceId.ToHexString());
        result.Callback.ActivityTraceId.ShouldBeNull();
    }

    [Fact]
    public async Task ClientIncomingOnly_SendsNothing_AndReadsCallbackHeaders()
    {
        await using var pair = await Pair.Create(clientPropagation: TelemetryPropagationPolicy.Incoming);
        using var activity = StartW3CActivity();

        var result = await pair.Proxy.ProbeViaCallback(TraceParentB);

        result.Server.ActivityTraceId.ShouldBeNull();
        result.Callback.ActivityTraceId.ShouldBe(TraceIdB);
    }

    [Fact]
    public async Task ServerIncomingOnly_ReadsCalls_AndSendsNothingWithCallbacks()
    {
        await using var pair = await Pair.Create(serverPropagation: TelemetryPropagationPolicy.Incoming);
        using var activity = StartW3CActivity();

        var result = await pair.Proxy.ProbeViaCallback();

        result.Server.ActivityTraceId.ShouldBe(activity.TraceId.ToHexString());
        result.Callback.ActivityTraceId.ShouldBeNull();
    }

    [Fact]
    public async Task ServerOutgoingOnly_IgnoresCallHeaders_AndSendsCallbacks()
    {
        await using var pair = await Pair.Create(serverPropagation: TelemetryPropagationPolicy.Outgoing);
        using var activity = StartW3CActivity();

        var result = await pair.Proxy.ProbeViaCallback(TraceParentB);

        result.Server.ActivityTraceId.ShouldBeNull();
        result.Callback.ActivityTraceId.ShouldBe(TraceIdB);
    }

    [Fact]
    public async Task ServerActivity_ComesFromTheIpcActivitySource_WhenListened()
    {
        var started = new List<Activity>();
        var baggageAtStart = new List<string>();
        using var listener = ListenToIpcSpans(out var stopped, activity =>
        {
            lock (started)
            {
                if (activity.Kind is ActivityKind.Server)
                {
                    started.Add(activity);
                    baggageAtStart.AddRange(activity.Baggage.Select(pair => $"{pair.Key}={pair.Value}"));
                }
            }
        });
        await using var pair = await Pair.Create();

        using (var caller = new Activity("caller").SetParentId(TraceParentA).Start())
        {
            caller.TraceStateString = TraceState;
            caller.AddBaggage("k", "v");
            var result = await pair.Proxy.Probe();
            result.ActivityBaggage.ShouldBe(["k=v"]);
        }

        var server = started.ShouldHaveSingleItem();
        server.Kind.ShouldBe(ActivityKind.Server);
        server.DisplayName.ShouldBe($"{nameof(ITraceProbe)}/{nameof(ITraceProbe.Probe)}");
        server.TraceId.ToHexString().ShouldBe(TraceIdA);
        server.TraceStateString.ShouldBe(TraceState);
        server.HasRemoteParent.ShouldBeTrue();
        baggageAtStart.ShouldBe(["k=v"]);
        lock (stopped)
        {
            stopped.ShouldContain(server);
        }
    }

    [Fact]
    public async Task ServerActivity_WrapsAHandlerOnACustomScheduler()
    {
        using var listener = ListenToIpcSpans(out var stopped);
        var scheduler = new ConcurrentExclusiveSchedulerPair().ExclusiveScheduler;
        await using var pair = await Pair.Create(scheduler: scheduler);
        using var caller = StartW3CActivity();

        var result = await pair.Proxy.Probe();

        // The handler ran on that scheduler, so the call took its path and not the default one.
        result.SchedulerId.ShouldBe(scheduler.Id);
        result.ActivityTraceId.ShouldBe(caller.TraceId.ToHexString());
        lock (stopped)
        {
            stopped.ShouldContain(span => span.Kind == ActivityKind.Client && span.SpanId.ToHexString() == result.ActivityParentSpanId);
            stopped.ShouldContain(span => span.Kind == ActivityKind.Server && span.SpanId.ToHexString() == result.ActivitySpanId);
        }
    }

    [Fact]
    public async Task ClientActivity_ComesFromTheIpcActivitySource_WhenListened()
    {
        using var listener = ListenToIpcSpans(out var stopped);
        await using var pair = await Pair.Create();
        using var caller = StartW3CActivity();

        var result = await pair.Proxy.Probe();

        Activity client;
        lock (stopped)
        {
            client = stopped.Where(activity => activity.ParentSpanId == caller.SpanId).ShouldHaveSingleItem();
        }
        client.Kind.ShouldBe(ActivityKind.Client);
        client.DisplayName.ShouldBe($"{nameof(ITraceProbe)}/{nameof(ITraceProbe.Probe)}");
        result.ActivityParentSpanId.ShouldBe(client.SpanId.ToHexString());
        Activity.Current.ShouldBeSameAs(caller);
    }

    [Fact]
    public async Task ClientIncomingOnly_StartsNoClientActivity()
    {
        using var listener = ListenToIpcSpans(out var stopped);
        await using var pair = await Pair.Create(clientPropagation: TelemetryPropagationPolicy.Incoming);
        using var caller = StartW3CActivity();

        var result = await pair.Proxy.Probe();

        result.ActivityTraceId.ShouldBeNull();
        lock (stopped)
        {
            stopped.ShouldNotContain(activity => activity.ParentSpanId == caller.SpanId);
        }
    }

    [Fact]
    public async Task AmbientActivityWithAnAllZeroTraceId_PassesOnOnlyItsBaggage()
    {
        await using var pair = await Pair.Create();
        using var activity = new Activity("caller").SetParentId(default(ActivityTraceId), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded).Start();
        activity.TraceStateString = TraceState;
        activity.AddBaggage("k", "v");

        var result = await pair.Proxy.Probe();

        // The propagator sends the all-zero traceparent, and the receiver drops it with its tracestate.
        SentHeaders()!["traceparent"].ShouldStartWith("00-00000000000000000000000000000000-");
        result.ActivityTraceId.ShouldNotBe(new string('0', 32));
        result.ActivityTraceState.ShouldBeNull();
        result.ActivityBaggage.ShouldBe(["k=v"]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void OutgoingTrace_DisposedWhereItsSpanIsNotCurrent_StopsIt_AndLeavesThatContextAsItFoundIt(bool poolThreadStopped, bool suppressed)
    {
        using var listener = ListenToIpcSpans(out var stopped);
        using var caller = StartW3CActivity();
        var trace = Telemetry.OutgoingTrace.Start("ITraceProbe/Probe")!;
        var span = Activity.Current!;
        // As on the pool thread that ends a call made under SuppressFlow, holding whatever other work left there.
        Activity.Current = null;
        using var poolThread = new Activity("pool thread").Start();
        if (poolThreadStopped)
        {
            // Stopped elsewhere, so it stays current here, and the Activity.Current setter would refuse it.
            ExecutionContext.Run(ExecutionContext.Capture()!, _ => poolThread.Stop(), null);
        }

        if (suppressed)
        {
            // As when code under SuppressFlow finishes the call inline: a Cancel, say.
            using (ExecutionContext.SuppressFlow())
            {
                trace.Dispose();
            }
        }
        else
        {
            trace.Dispose();
        }

        Activity.Current.ShouldBeSameAs(poolThread);
        stopped.ShouldContain(span);
    }

    [Fact]
    public async Task BeforeOutgoingCall_WhatItAddsToTheClientSpan_IsSent()
    {
        await using var pair = await Pair.Create(beforeOutgoingCall: (_, _) =>
        {
            Activity.Current!.AddBaggage("hook", "added");
            Activity.Current.TraceStateString = "hook=1";
            return Task.CompletedTask;
        });
        using var activity = StartW3CActivity();

        var result = await pair.Proxy.Probe();

        result.ActivityBaggage.ShouldBe(["hook=added"]);
        result.ActivityTraceState.ShouldBe("hook=1");
    }

    [Fact]
    public async Task AmbientTraceState_DropsMembersThatBreakTheGrammar()
    {
        await using var pair = await Pair.Create();
        using var activity = StartW3CActivity();
        activity.TraceStateString = "congo=t61rcWkgMzE,Bad=1";

        (await pair.Proxy.Probe()).ActivityTraceState.ShouldBe("congo=t61rcWkgMzE");
    }

    [Fact]
    public async Task FailedCalls_SetTheErrorStatusOnTheClientAndServerSpans()
    {
        using var listener = ListenToIpcSpans(out var stopped);
        await using var pair = await Pair.Create();
        using var caller = StartW3CActivity();

        await Should.ThrowAsync<RemoteException>(() => pair.Proxy.Fail());
        await pair.Proxy.FailOneWay();

        // The one-way server span stops last, after the call returns; spans left over from earlier tests may be there too.
        var spans = await StoppedOnceThereIs(ActivityKind.Server, nameof(ITraceProbe.FailOneWay));
        Status(ActivityKind.Client, nameof(ITraceProbe.Fail)).ShouldBe((ActivityStatusCode.Error, "probe failed"));
        Status(ActivityKind.Server, nameof(ITraceProbe.Fail)).ShouldBe((ActivityStatusCode.Error, "probe failed"));
        Status(ActivityKind.Client, nameof(ITraceProbe.FailOneWay)).ShouldBe((ActivityStatusCode.Unset, null));
        Status(ActivityKind.Server, nameof(ITraceProbe.FailOneWay)).ShouldBe((ActivityStatusCode.Error, "one-way probe failed"));

        (ActivityStatusCode, string?) Status(ActivityKind kind, string method)
        => spans.Single(span => span.Kind == kind && span.DisplayName == $"{nameof(ITraceProbe)}/{method}") is var span
            ? (span.Status, span.StatusDescription)
            : default;

        async Task<Activity[]> StoppedOnceThereIs(ActivityKind kind, string method)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                lock (stopped)
                {
                    if (stopped.Any(span => span.Kind == kind && span.DisplayName == $"{nameof(ITraceProbe)}/{method}"))
                    {
                        return stopped.ToArray();
                    }
                }
                await Task.Delay(10, timeout.Token);
            }
        }
    }

    [Fact]
    public async Task ClientTimeout_SetsTheCallersErrorOnTheClientSpan()
    {
        using var listener = ListenToIpcSpans(out var stopped);
        // The client's own timeout: a Message.RequestTimeout would also time the server out, with the same text.
        await using var pair = await Pair.Create(requestTimeout: TimeSpan.FromMilliseconds(200));
        using var caller = StartW3CActivity();

        var ex = await Should.ThrowAsync<TimeoutException>(() => pair.Proxy.Hang());

        Activity client;
        lock (stopped)
        {
            client = stopped.Single(span => span.Kind == ActivityKind.Client && span.DisplayName == $"{nameof(ITraceProbe)}/{nameof(ITraceProbe.Hang)}");
        }
        client.Status.ShouldBe(ActivityStatusCode.Error);
        client.StatusDescription.ShouldBe(ex.Message);
    }

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

    private static Activity StartW3CActivity() => new Activity("caller").SetIdFormat(ActivityIdFormat.W3C).Start();

    // The headers a call made now would send, from a client span of their own.
    private static Dictionary<string, string>? SentHeaders()
    {
        using var trace = Telemetry.OutgoingTrace.Start("ITraceProbe/Probe");
        return trace?.Headers();
    }

    // Samples every UiPath.Ipc span until disposed, and collects those that stop. A span can stop on another thread, so the list
    // is filled under its lock.
    private static ActivityListener ListenToIpcSpans(out List<Activity> stopped, Action<Activity>? started = null)
    {
        var spans = new List<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "UiPath.Ipc",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = started,
            ActivityStopped = activity => { lock (spans) spans.Add(activity); },
        };
        ActivitySource.AddActivityListener(listener);
        stopped = spans;
        return listener;
    }

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

        public Task<TraceProbeResult> Probe(Dictionary<string, string> headers) => Call(nameof(ITraceProbe.Probe), headers);

        // Frames are written by hand so the headers can break the rules the client enforces.
        public Task<TraceProbeResult> Call(string methodName, Dictionary<string, string> headers, params object[] arguments)
            => CallWithHeadersJson(methodName, Newtonsoft.Json.JsonConvert.SerializeObject(headers), arguments);

        public Task<TraceProbeResult> ProbeWithHeadersJson(string headers) => CallWithHeadersJson(nameof(ITraceProbe.Probe), headers);

        private async Task<TraceProbeResult> CallWithHeadersJson(string methodName, string headers, params object[] arguments)
        {
            var parameters = Newtonsoft.Json.JsonConvert.SerializeObject(arguments.Select(argument => IpcJsonSerializer.Instance.Serialize(argument)));
            var json = $$"""{"Endpoint":"{{nameof(ITraceProbe)}}","Id":"{{++_requestId}}","MethodName":"{{methodName}}","Parameters":{{parameters}},"TimeoutInSeconds":0,"Headers":{{headers}}}""";
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
            TelemetryPropagationPolicy serverPropagation = TelemetryPropagationPolicy.Both,
            TimeSpan? requestTimeout = null,
            BeforeCallHandler? beforeOutgoingCall = null,
            TaskScheduler? scheduler = null)
        {
            var pipeName = $"ipctest_trace_{Guid.NewGuid():N}";

            var server = new IpcServer
            {
                Transport = new NamedPipeServerTransport { PipeName = pipeName },
                Endpoints = new() { { typeof(ITraceProbe), new TraceProbe() } },
                TelemetryPropagation = serverPropagation,
                Scheduler = scheduler,
            };

            var client = new IpcClient
            {
                Transport = new NamedPipeClientTransport { PipeName = pipeName },
                Callbacks = new() { { typeof(ITraceProbeCallback), new TraceProbeCallback() } },
                TelemetryPropagation = clientPropagation,
                RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10),
                BeforeOutgoingCall = beforeOutgoingCall,
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
