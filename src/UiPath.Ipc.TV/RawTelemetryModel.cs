namespace UiPath.Ipc.TV;

internal readonly record struct RawTelemetryModel
{
    public required IReadOnlyDictionary<FileInfo, IReadOnlyList<RecordLine>> FileToRecordList { get; init; }
}

internal readonly record struct RecordLine(int LineNumber, Telemetry.RecordBase Record);
