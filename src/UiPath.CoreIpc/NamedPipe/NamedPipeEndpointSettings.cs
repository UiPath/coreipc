using System;
using System.IO.Pipes;

namespace UiPath.CoreIpc.NamedPipe
{
    public class NamedPipeEndpointSettings<TContract> : EndpointSettings where TContract : class
    {
        public NamedPipeEndpointSettings(TContract serviceInstance = null, Type callbackContract = null) : base(typeof(TContract), serviceInstance, callbackContract) { }
        public Action<PipeSecurity> AccessControl { get; set; }
    }

    public class NamedPipeEndpointSettings<TContract, TCallbackContract> : NamedPipeEndpointSettings<TContract> where TContract : class where TCallbackContract : class
    {
        public NamedPipeEndpointSettings(TContract serviceInstance = null) : base(serviceInstance, typeof(TCallbackContract)) {}
    }
}