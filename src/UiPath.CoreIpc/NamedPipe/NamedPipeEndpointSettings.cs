using System;
using System.IO.Pipes;

namespace UiPath.CoreIpc.NamedPipe
{
    public class NamedPipeEndpointSettings<TContract> : EndpointSettings where TContract : class
    {
        public NamedPipeEndpointSettings(string name, TContract serviceInstance = null, Type callbackContract = null) : base(name, typeof(TContract), serviceInstance, callbackContract) { }
        public Action<PipeSecurity> AccessControl { get; set; }
    }

    public class NamedPipeEndpointSettings<TContract, TCallbackContract> : NamedPipeEndpointSettings<TContract> where TContract : class where TCallbackContract : class
    {
        public NamedPipeEndpointSettings(string name, TContract serviceInstance = null) : base(name, serviceInstance, typeof(TCallbackContract)) {}
    }
}