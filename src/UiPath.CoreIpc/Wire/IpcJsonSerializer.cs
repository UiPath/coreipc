﻿using Newtonsoft.Json;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace UiPath.Ipc;

internal class IpcJsonSerializer : IArrayPool<char>
{
    public static readonly IpcJsonSerializer Instance = new();

    static readonly JsonSerializer StringArgsSerializer = new() { CheckAdditionalContent = true };

#if !NET461
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
    public async ValueTask<T?> DeserializeAsync<T>(Stream json, ILogger? logger)
    {
        using var stream = IOHelpers.GetStream((int)json.Length);
        await json.CopyToAsync(stream);
        stream.Position = 0;
        using var reader = CreateReader(new StreamReader(stream));
        var result = StringArgsSerializer.Deserialize<T>(reader);
        if (stream.Position != json.Length)
        {
            throw new InvalidOperationException("Buffer underrun detected.");
        }
        return result;
    }
    public void Serialize(object? obj, Stream stream) => Serialize(obj, new StreamWriter(stream), StringArgsSerializer);
    private void Serialize(object? obj, TextWriter streamWriter, JsonSerializer serializer)
    {
        using var writer = new JsonTextWriter(streamWriter) { ArrayPool = this, CloseOutput = false };
        serializer.Serialize(writer, obj);
        writer.Flush();
    }
    char[] IArrayPool<char>.Rent(int minimumLength) => ArrayPool<char>.Shared.Rent(minimumLength);
    void IArrayPool<char>.Return(char[]? array)
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
