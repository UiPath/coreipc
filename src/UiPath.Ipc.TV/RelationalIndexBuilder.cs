using Newtonsoft.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace UiPath.Ipc.TV;

internal sealed class RelationalIndexBuilder
{
    public static async Task<RelationalIndex> Build(DirectoryInfo dir, IProgress<RelationalIndexProgressReport>? progress = null, CancellationToken ct = default)
    {
        var instance = new RelationalIndexBuilder(dir, progress, ct);
        await Task.Run(instance.Run);
        return instance._output ?? throw new InvalidOperationException();
    }

    private readonly DirectoryInfo _dir;
    private readonly IProgress<RelationalIndexProgressReport>? _progress;
    private readonly CancellationToken _ct;

    private long _cTotalBytes;
    private long _cProcessedBytes;
    private RelationalIndex? _output;

    private RelationalIndexBuilder(DirectoryInfo dir, IProgress<RelationalIndexProgressReport>? progress, CancellationToken ct)
    {
        _dir = dir;
        _progress = progress;
        _ct = ct;
    }

    private async Task Run()
    {
        var files = _dir.EnumerateFiles("*.ndjson", SearchOption.TopDirectoryOnly).ToArray();

        var actualHash = ComputeSHA256(files);
        if (TryReadIndex(_dir) is { } existingIndex && existingIndex.Hash == actualHash)
        {
            _output = existingIndex;
            return;
        }

        _cTotalBytes = files.Sum(f => f.Length);

        var tuples = await Task.WhenAll(files.Select(Run));

        var orderedFiles = tuples
            .Select(x => x.fileIndex)
            .OrderBy(f => f.FileName);

        var fileNameToOrderedFileIndex = orderedFiles
            .Select((f, i) => (f, i))
            .ToDictionary(x => x.f.FileName, x => x.i);

        var orderedRecords = EnumerateOrderedRecords().ToArray();

        _output = new RelationalIndex()
        {
            Hash = actualHash,
            Files = tuples.ToDictionary(x => x.fileIndex.FileName, x => x.fileIndex),
            OrderedFileNames = orderedFiles.Select(f => f.FileName).ToArray(),
            TimeOrderedRecords = orderedRecords
        };

        IEnumerable<(int FileIndex, int RecordIndexInFile)> EnumerateOrderedRecords()
        => tuples
            .Select(fileIndexData => fileIndexData.infos.Select(recordInfo => (
                FileIndex: fileNameToOrderedFileIndex[fileIndexData.fileIndex.FileName],
                recordInfo,
                RecordIndexInFile: fileIndexData.fileIndex.IdToIndex[recordInfo.Id])))
            .SelectMany(x => x)
            .OrderBy(x => x.recordInfo.CreatedAtUtc)
            .Select(x => (x.FileIndex, x.RecordIndexInFile));
    }

    private static RelationalIndex? TryReadIndex(DirectoryInfo dir)
    {
        var path = Path.Combine(dir.FullName, "index.json");
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<RelationalIndex>(json);
        }
        catch
        {
            return null;
        }
    }
    private static string ComputeSHA256(IReadOnlyList<FileInfo> files)
    {
        var json = JsonConvert.SerializeObject(new
        {
            FileInfos = files
                .OrderBy(f => f.Name)
                .Select(f => new { f.Name, f.Length, f.LastWriteTimeUtc })
                .ToArray()
        });

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(hash);
    }

    private async Task<(RelationalFileIndex fileIndex, IReadOnlyList<RecordInfo> infos)> Run(FileInfo file)
    {
        using var stream = new BufferedStream(file.OpenRead(), 8192);

        var idToIndex = new Dictionary<string, int>();
        var offsets = new List<long>();

        var infos = new List<RecordInfo>();

        foreach (var (record, index, offset, length) in Enumerate(stream))
        {
            var info = record.GetInfo();
            infos.Add(info);

            idToIndex[info.Id] = index;
            offsets.Add(offset);

            _cProcessedBytes += length;
        }

        var fileIndex = new RelationalFileIndex()
        {
            FileName = file.Name,
            Offsets = offsets,
            IdToIndex = idToIndex
        };
        return (fileIndex, infos);

        static IEnumerable<(Telemetry.RecordBase recordBase, int index, long offset, int length)> Enumerate(Stream stream)
        {
            long offsetBehind = 0;
            int index = 0;
            long offsetAhead = -1;
            var buffer = new List<byte>();

            while (stream.ReadByte() is int b && b != -1)
            {
                offsetAhead++;

                if (b != '\n')
                {
                    buffer.Add((byte)b);
                    continue;
                }

                var unsafeSpan = CollectionsMarshal.AsSpan(buffer);
                if (unsafeSpan[^1] == '\r')
                {
                    unsafeSpan = unsafeSpan[..^1];
                }

                var json = Encoding.UTF8.GetString(unsafeSpan);
                Telemetry.RecordBase recordBase;
                try
                {
                    recordBase = JsonConvert.DeserializeObject<Telemetry.RecordBase>(json, Telemetry.Jss)!;
                }
                catch
                {
                    throw;
                }

                yield return (recordBase, index, offsetBehind, length: (int)(offsetAhead - offsetBehind));
                offsetBehind = offsetAhead;
                index++;
            }
        }
    }
}