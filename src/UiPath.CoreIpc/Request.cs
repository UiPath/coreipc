using Newtonsoft.Json;
using System;
using System.Globalization;
using System.Reflection;

namespace UiPath.CoreIpc
{
    class Request
    {
        public Request(string id, string methodName, string[] parameters, double timeoutInSeconds)
        {
            Id = id;
            MethodName = methodName;
            Parameters = parameters;
            TimeoutInSeconds = timeoutInSeconds;
        }
        public double TimeoutInSeconds { get; }
        public string Id { get; }
        public string MethodName { get; }
        public string[] Parameters { get; }
        public override string ToString() => $"{MethodName} {Id}.";
        internal TimeSpan GetTimeout(TimeSpan @default) => TimeoutInSeconds == 0 ? @default : TimeSpan.FromSeconds(TimeoutInSeconds);
    }
}