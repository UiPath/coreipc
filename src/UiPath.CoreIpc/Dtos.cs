﻿using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
namespace UiPath.CoreIpc
{
    public class Message
    {
        internal bool ObjectParameters { get; set; }
        internal Type CallbackContract { get; set; }
        [JsonIgnore]
        public IClient Client { get; set; }
        [JsonIgnore]
        public TimeSpan RequestTimeout { get; set; }
        public TCallbackInterface GetCallback<TCallbackInterface>() where TCallbackInterface : class => 
            Client.GetCallback<TCallbackInterface>(CallbackContract, ObjectParameters);
        public void ImpersonateClient(Action action) => Client.Impersonate(action);
    }
    public class Message<TPayload> : Message
    {
        public Message(TPayload payload) => Payload = payload;
        public TPayload Payload { get; }
    }
    class Request
    {
        public Request(string endpoint, string id, string methodName, string[] parameters, object[] objectParameters, double timeoutInSeconds, string parentId)
        {
            Endpoint = endpoint;
            Id = id;
            MethodName = methodName;
            Parameters = parameters;
            TimeoutInSeconds = timeoutInSeconds;
            ObjectParameters = objectParameters;
            ParentId = parentId;
        }
        public double TimeoutInSeconds { get; }
        public string Endpoint { get; }
        public string Id { get; }
        public string MethodName { get; }
        public string[] Parameters { get; }
        public object[] ObjectParameters { get; }
        internal Stream UploadStream { get; set; }

        public string ParentId { get; set; }

        public override string ToString() => $"{Endpoint} {MethodName} {Id}.";
        internal bool HasObjectParameters => ObjectParameters is not null;
        internal TimeSpan GetTimeout(TimeSpan defaultTimeout) => TimeoutInSeconds == 0 ? defaultTimeout : TimeSpan.FromSeconds(TimeoutInSeconds);
    }
    class CancellationRequest
    {
        public CancellationRequest(string requestId) => RequestId = requestId;
        public string RequestId { get; }
    }
    class Response
    {
        public Response(string requestId, string data = null, object objectData = null, Error error = null)
        {
            RequestId = requestId;
            Data = data;
            Error = error;
            ObjectData = objectData;
        }
        public string RequestId { get; }
        public string Data { get; }
        public object ObjectData { get; }
        public Error Error { get; }
        internal Stream DownloadStream { get; set; }
        public static Response Fail(Request request, Exception ex) => new(request.Id, error: new(ex));
        public static Response Success(Request request, string data) => new(request.Id, data);
        public static Response Success(Request request, Stream downloadStream) => new(request.Id) { DownloadStream = downloadStream };
        public TResult Deserialize<TResult>(ISerializer serializer, bool objectParameters)
        {
            if (Error != null)
            {
                throw new RemoteException(Error);
            }
            return (TResult)(DownloadStream ?? (objectParameters ?
                serializer.Deserialize(ObjectData, typeof(TResult)) : serializer.Deserialize(Data ?? "", typeof(TResult))));
        }
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
}