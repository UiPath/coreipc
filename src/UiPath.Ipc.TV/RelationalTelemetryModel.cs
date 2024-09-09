namespace UiPath.Ipc.TV;

public sealed class RelationalTelemetryModel
{
    public static readonly RelationalTelemetryModel Empty = new()
    {
        Records = new Dictionary<RecordId, RelationalRecord>(),
        TimestampOrder = [],
        IdToIndex = new Dictionary<RecordId, int>()
    };

    public required IReadOnlyDictionary<RecordId, RelationalRecord> Records { get; init; }

    public required IReadOnlyList<RelationalRecord> TimestampOrder { get; init; }

    public required IReadOnlyDictionary<RecordId, int> IdToIndex { get; init; }
}

internal readonly struct ModelFilter
{
    public required Func<Telemetry.RecordBase, bool> Predicate { get; init; }
}

internal readonly record struct FilterProgressReport(int CTotal, int CProcessed, int CPassed);

internal sealed class ModelFilterExecutor
{
    public static async Task<RelationalTelemetryModel> ExecuteAsync(RelationalTelemetryModel input, ModelFilter filter, IProgress<FilterProgressReport> progress)
    {
        var instance = new ModelFilterExecutor(input, filter, progress);
        await Task.Run(instance.RunAsync);
        return instance._output ?? throw new InvalidOperationException();
    }

    private readonly RelationalTelemetryModel _input;
    private readonly ModelFilter _filter;
    private readonly IProgress<FilterProgressReport> _progress;

    private RelationalTelemetryModel? _output;

    private ModelFilterExecutor(RelationalTelemetryModel input, ModelFilter filter, IProgress<FilterProgressReport> progress)
    {
        _input = input;
        _filter = filter;
        _progress = progress;
    }

    private async Task RunAsync()
    {
        var cTotal = _input.TimestampOrder.Count;
        var cProcessed = 0;
        var cPassed = 0;

        var list = new List<RelationalRecord>();

        foreach (var record in _input.TimestampOrder)
        {
            try
            {
                if (!_filter.Predicate(record.Record))
                {
                    continue;
                }
                cPassed++;

                list.Add(record);
            }
            finally
            {
                cProcessed++;
                _progress.Report(new(cTotal, cProcessed, cPassed));
            }
        }

        _output = new()
        {
            TimestampOrder = list,
            Records = list.ToDictionary(x => new RecordId( x.Record.Id.Value)),
            IdToIndex = list
                .Select((x, index) => (x.Record.Id, index))
                .ToDictionary(x => new RecordId(x.Id.Value), x => x.index)
        };
    }
}