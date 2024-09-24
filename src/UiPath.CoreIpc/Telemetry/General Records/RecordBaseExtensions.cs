using static UiPath.Ipc.Telemetry;

namespace UiPath.Ipc;

internal static class RecordBaseExtensions
{
    public static T Log<T>(this T record) where T : RecordBase
    {
        Telemetry.Log(record);
        return record;
    }

    public static RecordInfo GetInfo(this RecordBase record)
    => new(
        Id: record.Id.Value,
        CreatedAtUtc: record.CreatedAtUtc,
        IsOperationStart: record is IOperationStart,
        IsOperationEnd: record is IOperationEnd,
        IsOperationFailed: record is IOperationFailed,
        IsSubOperation: record is ISubOperation,
        IsExternallyTriggered: record is IExternallyTriggered,
        Links: record.EnumerateLinks().ToArray());

    public static VoidSucceeded CreateSucceeded(this RecordBase record)
    => (record as IVoidOperation)?.CreateSucceeded()
    ?? new VoidSucceeded { StartId = record.Id };

    public static VoidFailed CreateFailed(this RecordBase record, Exception? ex)
    => (record as IVoidOperation)?.CreateFailed(ex)
    ?? new VoidFailed { StartId = record.Id, Exception = ex };

    private static IEnumerable<RecordLink> EnumerateLinks(this RecordBase record)
    {
        if (record is Is<Effect> { Of: { } cause })
        {
            yield return new(RecordLinkRole.Cause, cause?.Value);
        }

        if (record is Is<SubOperation> { Of: { } parent })
        {
            yield return new(RecordLinkRole.Parent, parent?.Value);
        }

        if (record is Is<Modifier> { Of: { } modified })
        {
            yield return new(RecordLinkRole.Modified, modified?.Value);
        }

        if (record is Is<Success> { Of: { } success })
        {
            yield return new(RecordLinkRole.StartOfSuccess, success?.Value);
        }
        if (record is Is<Failure> { Of: { } failure })
        {
            yield return new(RecordLinkRole.StartOfFailure, failure?.Value);
        }
    }
}

public readonly record struct RecordInfo(
    string Id,
    DateTime CreatedAtUtc,
    bool IsOperationStart,
    bool IsOperationEnd,
    bool IsOperationFailed,
    bool IsSubOperation,
    bool IsExternallyTriggered,
    IReadOnlyList<RecordLink> Links);

public readonly record struct RecordLink(RecordLinkRole Role, string? Id);
public enum RecordLinkRole
{
    Cause,
    Parent,
    Modified,
    StartOfSuccess,
    StartOfFailure
}
