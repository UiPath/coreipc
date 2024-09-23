using Newtonsoft.Json;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace UiPath.Ipc;

public interface ISerializer
{
    ValueTask<T?> DeserializeAsync<T>(Stream json, ILogger? logger);
    void Serialize(object? obj, Stream stream);
    string Serialize(object? obj);
    object? Deserialize(string json, Type type);
}

internal interface ISerializerExtended : ISerializer
{
    ValueTask<T?> DeserializeAsync<T>(Stream json, Telemetry.ReceivedHeader telemCause, ILogger? logger);
}

internal class IpcJsonSerializer : ISerializerExtended, IArrayPool<char>
{
    public static readonly IpcJsonSerializer Instance = new();

    static readonly JsonSerializer StringArgsSerializer = new() { CheckAdditionalContent = true };

#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    public ValueTask<T?> DeserializeAsync<T>(Stream json, ILogger? logger) => DeserializeAsync<T>(json, telemCause: null!, logger);

#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    public async ValueTask<T?> DeserializeAsync<T>(Stream json, Telemetry.ReceivedHeader telemCause, ILogger? logger)
    {        
        var telemDeserializePayload = new Telemetry.DeserializePayload { ReceivedHeaderId = telemCause?.Id }.Log();
        return await telemDeserializePayload.Monitor(
            sanitizeSucceeded: (result, start) => new Telemetry.DeserializationSucceeded
            {
                Logger = logger,
                StartId = start.Id,
                ResultJson = JsonConvert.SerializeObject(result, Formatting.Indented)
            },
            async () =>
            {
                using var stream = IOHelpers.GetStream((int)json.Length);
                await json.CopyToAsync(stream);
                stream.Position = 0;
                using var reader = CreateReader(new StreamReader(stream));
                return StringArgsSerializer.Deserialize<T>(reader);
            });
    }
    public void Serialize(object? obj, Stream stream) => Serialize(obj, new StreamWriter(stream), StringArgsSerializer);
    private void Serialize(object? obj, TextWriter streamWriter, JsonSerializer serializer)
    {
        using var writer = new JsonTextWriter(streamWriter) { ArrayPool = this, CloseOutput = false };
        serializer.Serialize(writer, obj);
        writer.Flush();
    }
    public char[] Rent(int minimumLength) => ArrayPool<char>.Shared.Rent(minimumLength);
    public void Return(char[]? array)
    {
        if (array is null)
        {
            return;
        }

        ArrayPool<char>.Shared.Return(array);
    }

    public string Serialize(object? obj)
    {
        var stringWriter = new StringWriter(new StringBuilder(capacity: 256), CultureInfo.InvariantCulture);
        Serialize(obj, stringWriter, StringArgsSerializer);
        return stringWriter.ToString();
    }
    public object? Deserialize(string json, Type type)
    {
        using var reader = CreateReader(new StringReader(json));
        return StringArgsSerializer.Deserialize(reader, type);
    }
    private JsonTextReader CreateReader(TextReader json) => new(json) { ArrayPool = this };
}
