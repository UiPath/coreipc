using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Buffers;
namespace UiPath.CoreIpc;

public interface ISerializer
{
    ValueTask<T> DeserializeAsync<T>(Stream json);
    object Deserialize(object json, Type type);
    void Serialize(object obj, Stream stream);
}
class IpcJsonSerializer : ISerializer, IArrayPool<char>
{
    static readonly JsonSerializer ObjectArgsSerializer = new(){ DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate, NullValueHandling = NullValueHandling.Ignore, 
        CheckAdditionalContent = true };
#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    public async ValueTask<T> DeserializeAsync<T>(Stream json)
    {
        using var stream = IOHelpers.GetStream((int)json.Length);
        await json.CopyToAsync(stream);
        stream.Position = 0;
        using var reader = new JsonTextReader(new StreamReader(stream)) { ArrayPool = this };
        return ObjectArgsSerializer.Deserialize<T>(reader);
    }
    public object Deserialize(object json, Type type) => json switch
    {
        JToken token => token.ToObject(type, ObjectArgsSerializer),
        { } => type.IsAssignableFrom(json.GetType()) ? json : new JValue(json).ToObject(type),
        null => null,
    };
    public void Serialize(object obj, Stream stream)
    {
        using var writer = new JsonTextWriter(new StreamWriter(stream)) { ArrayPool = this, CloseOutput = false };
        ObjectArgsSerializer.Serialize(writer, obj);
        writer.Flush();
    }
    public char[] Rent(int minimumLength) => ArrayPool<char>.Shared.Rent(minimumLength);
    public void Return(char[] array) => ArrayPool<char>.Shared.Return(array);
}