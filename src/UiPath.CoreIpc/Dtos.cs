using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace UiPath.CoreIpc
{
    class Request
    {
        public Request(string endpoint, string id, string methodName, string[] parameters, double timeoutInSeconds)
        {
            Endpoint = endpoint;
            Id = id;
            MethodName = methodName;
            Parameters = parameters;
            TimeoutInSeconds = timeoutInSeconds;
        }
        public double TimeoutInSeconds { get; }
        public string Endpoint { get; }
        public string Id { get; }
        public string MethodName { get; }
        public string[] Parameters { get; }
        public override string ToString() => $"{Endpoint} {MethodName} {Id}.";
        internal TimeSpan GetTimeout(TimeSpan defaultTimeout) => TimeoutInSeconds == 0 ? defaultTimeout : TimeSpan.FromSeconds(TimeoutInSeconds);
    }
    class CancellationRequest
    {
        public CancellationRequest(string requestId) => RequestId = requestId;
        public string RequestId { get; }
    }
    class Response
    {
        [JsonConstructor]
        private Response(string requestId, string data, Error error)
        {
            RequestId = requestId;
            Data = data;
            Error = error;
        }
        public string RequestId { get; }
        public string Data { get; }
        public Error Error { get; }
        [JsonIgnore]
        public Stream DownloadStream { get; set; }
        public static Response Fail(Request request, string message) => Fail(request, new Exception(message));
        public static Response Fail(Request request, Exception ex) => new(request.Id, null, new(ex));
        public static Response Success(Request request, string data) => new(request.Id, data, null);
        public static Response Success(Request request, Stream downloadStream) => new(request.Id, null, null) { DownloadStream = downloadStream };
        public Response CheckError() => Error == null ? this : throw new RemoteException(Error);
    }
    [Serializable]
    public class Error
    {
        [JsonConstructor]
        private Error(string message, string stackTrace, string type, Error innerError)
        {
            Message = message;
            StackTrace = stackTrace;
            Type = type;
            InnerError = innerError;
        }
        public Error(Exception ex) : this(ex.Message, ex.StackTrace ?? ex.GetBaseException().StackTrace, GetExceptionType(ex),
            ex.InnerException == null ? null : new(ex.InnerException))
        {
        }
        public string Message { get; }
        public string StackTrace { get; }
        public string Type { get; }
        public Error InnerError { get; }
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
    readonly struct WireMessage
    {
        public WireMessage(MessageType messageType, byte[] data)
        {
            MessageType = messageType;
            Data = data;
        }
        public MessageType MessageType { get; }
        public byte[] Data { get; }
        public bool Empty => Data.Length == 0;
    }
}