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

    public override string ToString() => $"{Endpoint} {MethodName} {Id}.";

    public  TimeSpan GetTimeout(TimeSpan? defaultTimeout) => TimeoutInSeconds == 0 
        ? defaultTimeout.OrInfinite() 
        : TimeSpan.FromSeconds(TimeoutInSeconds);
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
    /// <summary>
    /// The selected <see cref="Exception.Data"/> entries of the exception this error was built from, or <c>null</c> when there are none.
    /// </summary>
    /// <remarks>
    /// On the wire this is a JSON object whose values are strings. The record's equality compares this dictionary by reference.
    /// </remarks>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyDictionary<string, string>? Data { get; init; }

    [return: NotNullIfNotNull("exception")]
    public static Error? FromException(Exception? exception) => FromException(exception, dataKeys: null);

    /// <summary>
    /// Like <see cref="FromException(Exception?)"/>, and also copies the <see cref="Exception.Data"/> entries listed in <paramref name="dataKeys"/>
    /// into <see cref="Data"/>, at every level of the inner exception chain. Only <see cref="string"/> values are copied; other values are skipped.
    /// </summary>
    [return: NotNullIfNotNull("exception")]
    public static Error? FromException(Exception? exception, IEnumerable<string>? dataKeys)
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

    private static string GetExceptionType(Exception exception) => (exception as RemoteException)?.Type ?? exception.GetType().FullName!;

    private static Dictionary<string, string>? GetData(Exception exception, IEnumerable<string>? dataKeys)
    {
        if (dataKeys is null)
        {
            return null;
        }

        Dictionary<string, string>? data = null;
        try
        {
            var exceptionData = exception.Data;
            foreach (var key in dataKeys)
            {
                try
                {
                    if (key is not null && exceptionData[key] is string value)
                    {
                        (data ??= [])[key] = value;
                    }
                }
                catch
                {
                    // Exception.Data is virtual: one faulty entry must not cost the caller the other entries.
                }
            }
        }
        catch
        {
            // A faulty Data getter or key collection must not cost the caller the error itself.
        }
        return data;
    }
}


public class RemoteException : Exception
{
    private const string StackTraceSeparator = "--- End of stack trace from previous location ---";
    private string? _remoteStackTrace = null;

    public RemoteException(Error error) : base(error.Message, error.InnerError == null ? null : new RemoteException(error.InnerError))
    {
        Type = error.Type;
        if (error.StackTrace is not null)
        {
            _remoteStackTrace = error.StackTrace.TrimEnd('\r', '\n');
        }
        if (error.Data is not null)
        {
            foreach (var pair in error.Data)
            {
                Data[pair.Key] = pair.Value;
            }
        }
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