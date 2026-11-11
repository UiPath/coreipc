using Newtonsoft.Json;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Text;

namespace UiPath.Ipc;

public class Message
{
    [JsonIgnore]
    public IClient Client { get; set; } = null!;
    [JsonIgnore]
    public TimeSpan RequestTimeout { get; set; }
}
public class Message<TPayload> : Message
{
    public Message(TPayload payload) => Payload = payload;
    public TPayload Payload { get; }
}
internal record Request(string Endpoint, string Id, string MethodName, string[] Parameters, double TimeoutInSeconds)
{
    [JsonIgnore]
    public Stream? UploadStream { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(RequestHeadersConverter))]
    public Dictionary<string, string?>? Headers { get; set; }

    public override string ToString() => $"{Endpoint} {MethodName} {Id}.";

    public  TimeSpan GetTimeout(TimeSpan? defaultTimeout) => TimeoutInSeconds == 0 
        ? defaultTimeout.OrInfinite() 
        : TimeSpan.FromSeconds(TimeoutInSeconds);
}
// Keeps the string values and skips the others, and a Headers that isn't an object, as JS and Python do: a peer that breaks
// the format must not fail the read, which would close the connection.
internal sealed class RequestHeadersConverter : JsonConverter<Dictionary<string, string?>>
{
    public override bool CanWrite => false;

    public override Dictionary<string, string?>? ReadJson(JsonReader reader, Type objectType, Dictionary<string, string?>? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType is not JsonToken.StartObject)
        {
            reader.Skip();
            return null;
        }
        var dateParseHandling = reader.DateParseHandling;
        // Otherwise a value that looks like a date is read as one, and skipped.
        reader.DateParseHandling = DateParseHandling.None;
        try
        {
            var headers = new Dictionary<string, string?>();
            while (reader.Read() && reader.TokenType is JsonToken.PropertyName)
            {
                var name = (string)reader.Value!;
                reader.Read();
                if (reader.TokenType is JsonToken.String)
                {
                    headers[name] = (string)reader.Value!;
                }
                else
                {
                    reader.Skip();
                }
            }
            if (reader.TokenType is not JsonToken.EndObject)
            {
                throw new JsonSerializationException($"Unexpected {reader.TokenType} in Headers.");
            }
            return headers;
        }
        finally
        {
            reader.DateParseHandling = dateParseHandling;
        }
    }

    public override void WriteJson(JsonWriter writer, Dictionary<string, string?>? value, JsonSerializer serializer) => throw new NotSupportedException();
}

record CancellationRequest(string RequestId);

internal record Response(string RequestId, string? Data = null, Error? Error = null)
{
    [JsonIgnore]
    public Stream? DownloadStream { get; set; }

    public static Response Fail(Request request, Exception ex, IEnumerable<string>? dataKeys) => new(request.Id, Error: Error.FromException(ex, dataKeys));
    public static Response Success(Request request, string data) => new(request.Id, data);
    public static Response Success(Request request, Stream downloadStream) => new(request.Id) { DownloadStream = downloadStream };
    public TResult Deserialize<TResult>()
    {        
        if (Error != null)
        {
            throw new RemoteException(Error);
        }

        return (TResult)(DownloadStream ?? IpcJsonSerializer.Instance.Deserialize(Data ?? "", typeof(TResult)))!;
    }
}

public record Error(string Message, string StackTrace, string Type, Error? InnerError)
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(DataConverter))]
    internal IReadOnlyDictionary<string, string>? Data { get; init; }

    [return: NotNullIfNotNull("exception")]
    public static Error? FromException(Exception? exception) => FromException(exception, dataKeys: null);

    [return: NotNullIfNotNull("exception")]
    internal static Error? FromException(Exception? exception, IEnumerable<string>? dataKeys)
    => exception is null
        ? null
        : new(
            Message: exception.Message,
            StackTrace: exception.StackTrace ?? exception.GetBaseException().StackTrace!,
            Type: GetExceptionType(exception),
            InnerError: FromException(exception.InnerException, dataKeys))
        {
            Data = GetData(exception, dataKeys)
        };
    public override string ToString() => new RemoteException(this).ToString();

    public virtual bool Equals(Error? other)
        => ReferenceEquals(this, other)
        || other is not null
            && EqualityContract == other.EqualityContract
            && Message == other.Message
            && StackTrace == other.StackTrace
            && Type == other.Type
            && InnerError == other.InnerError
            && DataEquals(Data, other.Data);

    public override int GetHashCode() => (Message, StackTrace, Type, InnerError).GetHashCode();

    private static bool DataEquals(IReadOnlyDictionary<string, string>? left, IReadOnlyDictionary<string, string>? right)
        => (left?.Count ?? 0) == (right?.Count ?? 0)
        && (left is null || right is null || left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value));

    private static string GetExceptionType(Exception exception) => (exception as RemoteException)?.Type ?? exception.GetType().FullName!;

    private static IReadOnlyDictionary<string, string>? GetData(Exception exception, IEnumerable<string>? dataKeys)
    {
        var received = (exception as RemoteException)?.ReceivedData;
        if (dataKeys is null)
        {
            return received;
        }

        // The listed keys go first: the caller keeps only the first entries it receives.
        var data = new Dictionary<string, string>();
        var listed = new HashSet<string>();
        try
        {
            var exceptionData = exception.Data;
            foreach (var key in dataKeys)
            {
                if (key is null)
                {
                    continue;
                }
                if (exceptionData[key] is string value)
                {
                    data[key] = value;
                }
                listed.Add(key);
            }
        }
        catch (Exception ex)
        {
            // A faulty Data getter, entry or key collection must not cost the caller the error itself.
            Trace.TraceError($"Failed to read the Exception.Data entries to send with the error. Ex: {ex}");
        }
        if (received is not null)
        {
            foreach (var pair in received)
            {
                if (!listed.Contains(pair.Key))
                {
                    data[pair.Key] = pair.Value;
                }
            }
        }
        return data.Count > 0 ? data : null;
    }

    // Keeps the string values and skips any other without loading it, so a peer that sends richer ones costs nothing and doesn't fail the read,
    // which would close the connection. A comment does fail it: no peer we stay compatible with writes one.
    private sealed class DataConverter : JsonConverter<IReadOnlyDictionary<string, string>>
    {
        public override bool CanWrite => false;

        public override IReadOnlyDictionary<string, string>? ReadJson(JsonReader reader, Type objectType, IReadOnlyDictionary<string, string>? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var dateParseHandling = reader.DateParseHandling;
            // Otherwise a string that looks like a date is read as a date, and skipped.
            reader.DateParseHandling = DateParseHandling.None;
            try
            {
                if (reader.TokenType is JsonToken.Comment)
                {
                    throw new JsonSerializationException("A comment isn't allowed in Error.Data.");
                }
                if (reader.TokenType is not JsonToken.StartObject)
                {
                    reader.Skip();
                    return null;
                }
                var data = new Dictionary<string, string>();
                while (reader.Read() && reader.TokenType is JsonToken.PropertyName)
                {
                    var key = (string)reader.Value!;
                    reader.Read();
                    if (reader.TokenType is JsonToken.String)
                    {
                        data[key] = (string)reader.Value!;
                    }
                    else
                    {
                        data.Remove(key);
                        reader.Skip();
                    }
                }
                if (reader.TokenType is not JsonToken.EndObject)
                {
                    throw new JsonSerializationException($"Unexpected {reader.TokenType} in Error.Data.");
                }
                return data;
            }
            finally
            {
                reader.DateParseHandling = dateParseHandling;
            }
        }

        public override void WriteJson(JsonWriter writer, IReadOnlyDictionary<string, string>? value, JsonSerializer serializer) => throw new NotSupportedException();
    }
}


public class RemoteException : Exception
{
    private const string StackTraceSeparator = "--- End of stack trace from previous location ---";
    // Exception.Data is a linked list, so copying n entries into it costs O(n²): a peer must not choose n.
    internal const int MaxReceivedDataEntries = 64;
    private string? _remoteStackTrace = null;

    public RemoteException(Error error) : base(error.Message, error.InnerError == null ? null : new RemoteException(error.InnerError))
    {
        Type = error.Type;
        if (error.StackTrace is not null)
        {
            _remoteStackTrace = error.StackTrace.TrimEnd('\r', '\n');
        }
        ReceivedData = Cap(error.Data);
        if (ReceivedData is not null)
        {
            foreach (var pair in ReceivedData)
            {
                Data[pair.Key] = pair.Value;
            }
        }
    }

    internal IReadOnlyDictionary<string, string>? ReceivedData { get; }

    private static IReadOnlyDictionary<string, string>? Cap(IReadOnlyDictionary<string, string>? data)
    {
        if (data is not { Count: > MaxReceivedDataEntries })
        {
            return data;
        }
        Trace.TraceError($"Received {data.Count} Exception.Data entries; keeping the first {MaxReceivedDataEntries}.");
        return data.Take(MaxReceivedDataEntries).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    public override string? StackTrace => _remoteStackTrace is null 
        ? base.StackTrace
        : $"{_remoteStackTrace}\r\n{StackTraceSeparator}\r\n{base.StackTrace}";

    public string Type { get; }
    public new RemoteException? InnerException => base.InnerException as RemoteException;
    public override string ToString()
    {
        var result = new StringBuilder();
        GatherInnerExceptions(result);
        return result.ToString();
    }
    private void GatherInnerExceptions(StringBuilder result)
    {
        result.Append($"{nameof(RemoteException)} wrapping {Type}: {Message} ");
        if (InnerException == null)
        {
            result.Append("\n");
        }
        else
        {
            result.Append(" ---> ");
            InnerException.GatherInnerExceptions(result);
            result.Append("\n\t--- End of inner exception stack trace ---\n");
        }
        result.Append(StackTrace);
    }
    public bool Is<TException>() where TException : Exception => Type == typeof(TException).FullName;
}
internal enum MessageType : byte { Request, Response, CancellationRequest, UploadRequest, DownloadResponse }