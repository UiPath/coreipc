using Newtonsoft.Json;
using System.Reactive.Subjects;

namespace UiPath.Ipc.TV;

public class FormProjectModel
{
    private readonly BehaviorSubject<FormProjectModelState> _state = new(FormProjectModelState.Idle);
    public ISubject<FormProjectModelState> State => _state;

    private readonly BehaviorSubject<RelationalTelemetryModel> _relationalModels = new(RelationalTelemetryModel.Empty);
    internal ISubject<RelationalTelemetryModel> RelationalModels => _relationalModels;

    private readonly Lazy<Task> _loading;

    private readonly IProjectContext _context;

    public FormProjectModel(IProjectContext context)
    {
        _context = context;
        _loading = new(Load);
    }

    public void EnsureLoading()
    {
        _ = _loading.Value;
    }

    private async Task Load()
    {
        _state.OnNext(FormProjectModelState.Reading);

        var rawModel = await ReadRawModel();

        _state.OnNext(FormProjectModelState.Linking);

        var relationalModel = await RelationalTelemetryModelBuilder.BuildAsync(rawModel);
        _relationalModels.OnNext(relationalModel);

        _state.OnNext(FormProjectModelState.Ready);
    }

    private async Task<RawTelemetryModel> ReadRawModel()
    {
        var filePaths = Directory.GetFiles(_context.ProjectPath, "*.ndjson");
        var fileData = await Task.WhenAll(filePaths.Select(ReadFile));

        return new()
        {
            FileToRecordList = fileData.ToDictionary(x => x.file, x => x.recordLines)
        };

        async Task<(FileInfo file, IReadOnlyList<RecordLine> recordLines)> ReadFile(string filePath)
        {
            return (
                new FileInfo(filePath), 
                await EnumerateRecordLines(filePath).ToListAsync());

            static async IAsyncEnumerable<RecordLine> EnumerateRecordLines(string filePath)
            {
                await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fileStream, leaveOpen: true);

                int lineIndex = 1;
                while (await reader.ReadLineAsync() is { } line)
                {
                    var record = JsonConvert.DeserializeObject<Telemetry.RecordBase>(line, Telemetry.Jss);
                    yield return new(lineIndex++, record!);
                }
            }
        }
    }

    //private async Task ScanOld()
    //{
    //    _state.OnNext(FormProjectModelState.Scanning);

    //    var filePaths = Directory.GetFiles(_context.ProjectPath, "*.ndjson");
    //    var lines = await Task.WhenAll(filePaths.Select(ScanFile));
    //    var cursors = lines.Select(_ => 0).ToArray();

    //    var advanced = true;
    //    var minTimestamp = null as DateTime?;
    //    var merged = new List<Line>();
    //    while (advanced)
    //    {
    //        advanced = false;
    //        var minCursor = -1;
    //        minTimestamp = null;
    //        for (int i = 0; i < cursors.Length; i++)
    //        {
    //            if (cursors[i] >= lines[i].Count)
    //            {
    //                continue;
    //            }

    //            if (minTimestamp is null)
    //            {
    //                minTimestamp = lines[i][cursors[i]].Record.CreatedAtUtc;
    //                minCursor = i;
    //            }
    //            else if (lines[i][cursors[i]].Record.CreatedAtUtc < minTimestamp)
    //            {
    //                minTimestamp = lines[i][cursors[i]].Record.CreatedAtUtc;
    //                minCursor = i;
    //            }
    //        }
    //        if (minCursor > -1)
    //        {
    //            advanced = true;
    //            merged.Add(lines[minCursor][cursors[minCursor]]);
    //            cursors[minCursor]++;
    //        }

    //        await Task.Yield();
    //    }

    //    _relationalModels.OnNext(new() { Lines = merged });
    //    _state.OnNext(FormProjectModelState.Ready);

    //    async Task<IReadOnlyList<Line>> ScanFile(string filePath)
    //    {
    //        var fileName = Path.GetFileName(filePath);

    //        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    //        using var reader = new StreamReader(fileStream, leaveOpen: true);

    //        var result = new List<Line>();
    //        while (await reader.ReadLineAsync() is { } input)
    //        {
    //            var record = JsonConvert.DeserializeObject<Telem.RecordBase>(input, Telem.Jss)!;
    //            var line = new Line { FilePath = filePath, FileName = fileName, Record = record };
    //            result.Add(line);
    //        }
    //        return result;
    //    }
    //}
}

public enum FormProjectModelState
{
    Idle,
    Reading,
    Linking,
    Ready
}

internal sealed class OrderedLineList
{
    public static readonly OrderedLineList Empty = new() { Lines = [] };

    public required IReadOnlyList<Line> Lines { get; init; }
}

internal readonly record struct Line
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required Telemetry.RecordBase Record { get; init; }
}

internal static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> values)
    {
        var list = new List<T>();
        await foreach (var value in values)
        {
            list.Add(value);
        }
        return list;
    }
}