using System.Diagnostics.CodeAnalysis;
using System.Text;
using Newtonsoft.Json;

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

    public  TimeSpan GetTimeout(TimeSpan defaultTimeout) => TimeoutInSeconds == 0 ? defaultTimeout : TimeSpan.FromSeconds(TimeoutInSeconds);
}
record CancellationRequest(string RequestId);

internal record Response(string RequestId, string? Data = null, Error? Error = null)
{
    [JsonIgnore]
    public Stream? DownloadStream { get; set; }

    public static Response Fail(Request request, Exception ex) => new(request.Id, Error: ex.ToError());
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
    [return: NotNullIfNotNull("exception")]
    public static Error? FromException(Exception? exception)
    => exception is null 
        ? null 
        : new(
            Message: exception.Message, 
            StackTrace: exception.StackTrace ?? exception.GetBaseException().StackTrace!, 
            Type: GetExceptionType(exception), 
            InnerError: FromException(exception.InnerException));
    public override string ToString() => new RemoteException(this).ToString();

    private static string GetExceptionType(Exception exception) => (exception as RemoteException)?.Type ?? exception.GetType().FullName!;
}

public class RemoteException : Exception
{
    public RemoteException(Error error) : base(error.Message, error.InnerError == null ? null : new RemoteException(error.InnerError))
    {
        Type = error.Type;
        StackTrace = error.StackTrace;
    }
    public string Type { get; }
    public override string StackTrace { get; }
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