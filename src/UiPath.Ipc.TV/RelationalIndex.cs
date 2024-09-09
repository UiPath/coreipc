namespace UiPath.Ipc.TV;

internal class RelationalIndex
{
    public required string Hash { get; init; }

    public required IReadOnlyDictionary<string, RelationalFileIndex> Files { get; init; }

    public required IReadOnlyList<string> OrderedFileNames { get; init; }
    public required IReadOnlyList<(int FileIndex, int RecordIndexInFile)> TimeOrderedRecords { get; init; }
}

internal class RelationalFileIndex
{
    public required string FileName { get; init; }
    public required IReadOnlyList<long> Offsets { get; init; }
    public required IReadOnlyDictionary<string, int> IdToIndex { get; init; }
}

internal readonly record struct RelationalIndexProgressReport(long CTotal, long CProcessed);
