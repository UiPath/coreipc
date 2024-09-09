namespace UiPath.Ipc.TV;

using static Telemetry;

public sealed class RelationalRecord
{
    public required RelationalTelemetryModel Model { get; set; }

    public required RecordBase Record { get; init; }
    public required Origin Origin { get; init; }

    public required IReadOnlyDictionary<ForwardLinkType, RecordReference> Links { get; init; }
    public required IReadOnlyDictionary<BackwardLinkType, IReadOnlySet<RelationalRecord>> ReversedLinks { get; init; }

    private readonly Lazy<int> _visualIndentation;

    public int GetVisualIndentation() => _visualIndentation.Value;

    public RelationalRecord()
    {
        _visualIndentation = new(() => MaybeGetVisualParent()?.GetVisualIndentation() + 1 ?? 0);
    }

    public RelationalRecord? MaybeGetVisualParent()
    {
        if (Links.TryGetValue(ForwardLinkType.Parent, out var reference) && reference is { Resolved: { } parent })
        {
            return parent;
        }

        if (Links.TryGetValue(ForwardLinkType.Cause, out reference) && reference is { Resolved: { } cause })
        {
            return cause;
        }

        return null;
    }

    public bool IsError(out Telemetry.ExceptionInfo? exceptionInfo)
    {
        if (Record is IOperationFailed operationFailed)
        {
            exceptionInfo = operationFailed.Exception;
            return true;
        }

        exceptionInfo = null;
        return false;
    }

}

public readonly record struct Origin(FileInfo File, int LineNumber);

public readonly record struct RecordId(string Value)
{
    public static implicit operator RecordId(string value) => new(value);
    public static implicit operator string(RecordId recordId) => recordId.Value;
}

public enum ForwardLinkType
{
    Parent,
    Cause,
    Modified,
    Succeeded,
    Failed
}

public enum BackwardLinkType
{
    Child,
    Effect,
    Modifier,
    Success,
    Failure
}

public readonly record struct RecordReference(RecordId Id, RelationalRecord? Resolved);

public static class ForwardLinkExtensions
{
    public static BackwardLinkType ToBackwardLink(this ForwardLinkType forwardLink) => forwardLink switch
    {
        ForwardLinkType.Parent => BackwardLinkType.Child,
        ForwardLinkType.Cause => BackwardLinkType.Effect,
        ForwardLinkType.Modified => BackwardLinkType.Modifier,
        ForwardLinkType.Succeeded => BackwardLinkType.Success,
        ForwardLinkType.Failed => BackwardLinkType.Failure,
        _ => throw new ArgumentOutOfRangeException(nameof(forwardLink)),
    };
}