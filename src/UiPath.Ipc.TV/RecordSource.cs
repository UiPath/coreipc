using Newtonsoft.Json;
using Nito.Disposables;
using System.Buffers;
using System.Text;

namespace UiPath.Ipc.TV;

internal class RecordSource : IDisposable
{
    private readonly DirectoryInfo _dir;
    private readonly RelationalIndex _index;
    private readonly Dictionary<int, FileStream> _streams = new();

    public RecordSource(DirectoryInfo dir, RelationalIndex index)
    {
        _dir = dir;
        _index = index;
    }

    public Telemetry.RecordBase this[int index]
    {
        get
        {
            var (fileIndex, recordIndexInFile) = _index.TimeOrderedRecords[index];
            var file = _index.OrderedFileNames[fileIndex];
            var fileIndexData = _index.Files[file];
            var offset = fileIndexData.Offsets[recordIndexInFile];

            var stream = GetStream(fileIndex) as Stream;
            stream.Seek(offset, SeekOrigin.Begin);

            int length;
            if (recordIndexInFile < fileIndexData.Offsets.Count - 1)
            {
                var nextOffset = fileIndexData.Offsets[recordIndexInFile + 1];
                length = (int)(nextOffset - offset);
                stream = new NestedStream(stream, length);
            }
            else
            {
                length = (int)(stream.Length - offset);
            }

            var bytes = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                stream.ReadExactly(bytes.AsSpan());
                var json = Encoding.UTF8.GetString(bytes);
                return JsonConvert.DeserializeObject<Telemetry.RecordBase>(json, Telemetry.Jss)!;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }
        }
    }

    private FileStream GetStream(int fileIndex)
    {
        if (!_streams.TryGetValue(fileIndex, out var stream))
        {
            var file = _index.OrderedFileNames[fileIndex];
            stream = new FileStream(Path.Combine(_dir.FullName, file), FileMode.Open, FileAccess.Read, FileShare.Read);
            _streams.Add(fileIndex, stream);
        }
        return stream;
    }

    public void Dispose() => new CollectionDisposable(_streams.Values).Dispose();
}
