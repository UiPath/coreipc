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
    internal Message SetValues(EndpointSettings endpoint, IClient client)
    {
        CallbackContract = endpoint.CallbackContract;
        Client = client;
        return this;
    }
}
public class Message<TPayload> : Message
{
    public Message(TPayload payload) => Payload = payload;
    public TPayload Payload { get; }
}
public record struct Request(int Id, string Method, string Endpoint, double Timeout)
{
    internal bool IsUpload => Parameters is [Stream, ..];
    internal object[] Parameters { get; set; }
    public override string ToString() => $"{Endpoint} {Method} {Id}.";
    internal TimeSpan GetTimeout(TimeSpan defaultTimeout) => Timeout == 0 ? defaultTimeout : TimeSpan.FromSeconds(Timeout);
}
public readonly record struct CancellationRequest(int RequestId);
public readonly record struct Response(int RequestId, Error Error = null);
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
enum MessageType : byte { Request, Response, CancellationRequest }