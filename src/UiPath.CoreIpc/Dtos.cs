using System.Runtime.Serialization;
using System.Text;
namespace UiPath.CoreIpc;
public class Message
{
    internal Type CallbackContract { get; set; }
    [IgnoreDataMember]
    public IClient Client { get; set; }
    [IgnoreDataMember]
    public TimeSpan RequestTimeout { get; set; }
    public TCallbackInterface GetCallback<TCallbackInterface>() where TCallbackInterface : class => Client.GetCallback<TCallbackInterface>(CallbackContract);
    public void ImpersonateClient(Action action) => Client.Impersonate(action);
}
public class Message<TPayload> : Message
{
    public Message(TPayload payload) => Payload = payload;
    public TPayload Payload { get; }
}
public record Request(string Endpoint, int Id, string MethodName, double TimeoutInSeconds)
{
    internal Type ResponseType { get; init; }
    internal object[] Parameters { get; set; }
    public override string ToString() => $"{Endpoint} {MethodName} {Id}.";
    internal TimeSpan GetTimeout(TimeSpan defaultTimeout) => TimeoutInSeconds == 0 ? defaultTimeout : TimeSpan.FromSeconds(TimeoutInSeconds);
}
public record CancellationRequest(int RequestId);
public record Response(int RequestId, Error Error = null)
{
    internal object Data { get; set; }
    public static Response Fail(Request request, Exception ex) => new(request.Id, ex.ToError());
}
[Serializable]
public record Error(string Message, string StackTrace, string Type, Error InnerError)
{
    public override string ToString() => new RemoteException(this).ToString();
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