using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Buffers;
using System.Globalization;
using System.Text;
namespace UiPath.CoreIpc;

public interface ISerializer
{
    ValueTask<T> DeserializeAsync<T>(Stream json);
    object Deserialize(object json, Type type);
    void Serialize(object obj, Stream stream);
    string Serialize(object obj);
    object Deserialize(string json, Type type);
}
class IpcJsonSerializer : ISerializer, IArrayPool<char>
{
    static readonly JsonSerializer DefaultSerializer = new(){ DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate, NullValueHandling = NullValueHandling.Ignore, 
        CheckAdditionalContent = true };
    static readonly JsonSerializer StringArgsSerializer = new(){ CheckAdditionalContent = true };
#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    public async ValueTask<T> DeserializeAsync<T>(Stream json)
    {
        using var stream = IOHelpers.GetStream((int)json.Length);
        await json.CopyToAsync(stream);
        stream.Position = 0;
        using var reader = CreateReader(new StreamReader(stream));
        return DefaultSerializer.Deserialize<T>(reader);
    }
    public object Deserialize(object json, Type type) => json switch
    {
        JToken token => token.ToObject(type, DefaultSerializer),
        { } => type.IsAssignableFrom(json.GetType()) ? json : new JValue(json).ToObject(type),
        null => null,
    };
    public void Serialize(object obj, Stream stream) => Serialize(obj, new StreamWriter(stream), DefaultSerializer);
    private void Serialize(object obj, TextWriter streamWriter, JsonSerializer serializer)
    {
        using var writer = new JsonTextWriter(streamWriter) { ArrayPool = this, CloseOutput = false };
        serializer.Serialize(writer, obj);
        writer.Flush();
    }
    public char[] Rent(int minimumLength) => ArrayPool<char>.Shared.Rent(minimumLength);
    public void Return(char[] array) => ArrayPool<char>.Shared.Return(array);
    public string Serialize(object obj)
    {
        var stringWriter = new StringWriter(new StringBuilder(capacity: 256), CultureInfo.InvariantCulture);
        Serialize(obj, stringWriter, StringArgsSerializer);
        return stringWriter.ToString();
    }
    public object Deserialize(string json, Type type)
    {
        using var reader = CreateReader(new StringReader(json));
        return DefaultSerializer.Deserialize(reader, type);
    }
    private JsonTextReader CreateReader(TextReader json) => new(json){ ArrayPool = this };
}