using System.Diagnostics.CodeAnalysis;
using System.Text;
using Newtonsoft.Json;
namespace UiPath.CoreIpc;
public class Message
{
    internal Type CallbackContract { get; set; }
    [JsonIgnore]
    public IClient Client { get; set; }
    [JsonIgnore]
    public TimeSpan RequestTimeout { get; set; }
    public TCallbackInterface GetCallback<TCallbackInterface>() where TCallbackInterface : class => 
        Client.GetCallback<TCallbackInterface>(CallbackContract);
    public void ImpersonateClient(Action action) => Client.Impersonate(action);
}
public class Message<TPayload> : Message
{
    public Message(TPayload payload) => Payload = payload;
    public TPayload Payload { get; }
}
record Request(string Endpoint, string Id, string MethodName, string[] Parameters, double TimeoutInSeconds)
{
    internal Stream UploadStream { get; set; }
    public override string ToString() => $"{Endpoint} {MethodName} {Id}.";
    internal TimeSpan GetTimeout(TimeSpan defaultTimeout) => TimeoutInSeconds == 0 ? defaultTimeout : TimeSpan.FromSeconds(TimeoutInSeconds);
}
record CancellationRequest(string RequestId);
record Response(string RequestId, string Data = null, Error Error = null)
{
    internal Stream DownloadStream { get; set; }
    public static Response Fail(Request request, Exception ex) => new(request.Id, Error: ex.ToError());
    public static Response Success(Request request, string data) => new(request.Id, data);
    public static Response Success(Request request, Stream downloadStream) => new(request.Id) { DownloadStream = downloadStream };
    public TResult Deserialize<TResult>(ISerializer serializer)
    {
        if (Error != null)
        {
            throw new RemoteException(Error);
        }
        return (TResult)(DownloadStream ?? serializer.Deserialize(Data ?? "", typeof(TResult)));
    }
}
[Serializable]
public record Error(string Message, string StackTrace, string Type, Error InnerError)
{
    [return: NotNullIfNotNull("exception")]
    public static Error? FromException(Exception? exception)
    => exception is null 
        ? null 
        : new(
            Message: exception.Message, 
            StackTrace: exception.StackTrace ?? exception.GetBaseException().StackTrace, 
            Type: GetExceptionType(exception), 
            InnerError: FromException(exception.InnerException));
    public override string ToString() => new RemoteException(this).ToString();

    private static string GetExceptionType(Exception exception) => (exception as RemoteException)?.Type ?? exception.GetType().FullName;
}
[Serializable]
public class RemoteException : Exception
{
    public RemoteException(Error error) : base(error.Message, error.InnerError == null ? null : new RemoteException(error.InnerError))
    {
        Type = error.Type;
        StackTrace = error.StackTrace;
    }
    public string Type { get; }
    public override string StackTrace { get; }
    public new RemoteException InnerException => (RemoteException)base.InnerException;
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
enum MessageType : byte { Request, Response, CancellationRequest, UploadRequest, DownloadResponse }