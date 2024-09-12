namespace UiPath.Ipc.TV;

public class OutgoingCallInfo
{
    public required OutgoingCallDetails Details { get; init; }

    public string Caller => $"{Details.ProcessStart.Record.Name} [Pid={Details.ProcessStart.Record.ProcessId}]";
    public string Callee => Details.EnsureConnection?.Record.ClientTransport.ToString() ?? $"Callback over {Details.ServiceClient.Record.CallbackServerConfig}";

    public string Method => Details.InvokeRemote.Record.Method;
    public IReadOnlyList<string> SerializedArgs => Details.InvokeRemoteProper.Record.SerializedArgs;

    public DateTime StartedAtUtc => Details.InvokeRemoteProper.Record.CreatedAtUtc;
    public TimeSpan? Duration => (Details.InvokeRemoteProperSucceded?.Record.CreatedAtUtc ?? Details.InvokeRemoteProperFailed?.Record.CreatedAtUtc) - StartedAtUtc;

    public string Call => $"{Method}({string.Join(", ", SerializedArgs)})";

    public override string ToString() => Call;
}

public class OutgoingCallDetails
{
    public required Relational<Telemetry.InvokeRemoteProper> InvokeRemoteProper { get; init; }
    public required Relational<Telemetry.ResultSucceeded>? InvokeRemoteProperSucceded { get; init; }
    public required Relational<Telemetry.VoidFailed>? InvokeRemoteProperFailed { get; init; }

    public required Relational<Telemetry.ProcessStart> ProcessStart { get; init; }
    public required Relational<Telemetry.ServiceClientCreated> ServiceClient { get; init; }
    public required Relational<Telemetry.InvokeRemote> InvokeRemote { get; init; }
    public required Relational<Telemetry.EnsureConnection>? EnsureConnection { get; init; }
}

public readonly record struct OutgoingCallInfoResults(IReadOnlyList<OutgoingCallInfo> Infos, IReadOnlyList<Exception> Exceptions);

internal class InvokeRemoteProperInfo
{
    public required Relational<Telemetry.InvokeRemoteProper> InvokeRemoteProper { get; init; }
    public Relational<Telemetry.ResultSucceeded>? InvokeRemoteProperSucceded { get; set; }
    public Relational<Telemetry.VoidFailed>? InvokeRemoteProperFailed { get; set; }

    public Relational<Telemetry.InvokeRemote>? InvokeRemote { get; set; }
    public Relational<Telemetry.ServiceClientCreated>? ServiceClientCreated { get; set; }

    public ServiceClientDestination? Destination { get; set; }
}

internal class ServiceClientInfo
{
    public required Relational<Telemetry.ServiceClientCreated> ServiceClientCreated { get; init; }
    public ServiceClientDestination? LatestConnect { get; set; }
}

internal sealed class ServiceClientDestination
{
    public required Relational<Telemetry.EnsureConnection> EnsureConnection { get; init; }
    public required Relational<Telemetry.EnsureConnectionInitialState> EnsureConnectionInitialState { get; init; }
    public required Relational<Telemetry.Connect> Connect { get; init; }
    public required Relational<Telemetry.VoidSucceeded> ConnectSuccess { get; init; }
}

public readonly record struct Relational<T> where T : Telemetry.RecordBase
{
    public required T Record { get; init; }
    public required RelationalRecord RelationalRecord { get; init; }
}


internal static class RelationalRecordExtensions
{
    public static bool Is<T>(this RelationalRecord relational, out Relational<T> record) where T : Telemetry.RecordBase
    {
        if (relational.Record is T asT)
        {
            record = new() { Record = asT, RelationalRecord = relational };
            return true;
        }

        record = default;
        return false;
    }

    public static bool Has<T>(this RelationalRecord relational, ForwardLinkType linkType, out Relational<T> other)
        where T : Telemetry.RecordBase
    {
        if (relational.Links.TryGetValue(linkType, out var reference) &&
            reference.Resolved is { } resolved &&
            resolved.Record is T otherRecord)
        {
            other = new() { Record = otherRecord, RelationalRecord = resolved };
            return true;
        }

        other = default;
        return false;
    }

    public static V GetValueOrAdd<K, V>(this Dictionary<K, V> dictionary, K key, Func<K, V> factory) where K : class
    {
        if (!dictionary.TryGetValue(key, out var value))
        {
            value = factory(key);
            dictionary[key] = value;
        }
        return value;
    }
}