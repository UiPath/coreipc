using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace UiPath.Ipc.NamedPipe
{
    public abstract class NamedPipeClientBuilderBase<TDerived, TInterface> : ServiceClientBuilder<TDerived, TInterface> where TInterface : class where TDerived : ServiceClientBuilder<TDerived, TInterface>
    {
        private readonly string _pipeName;
        private bool _allowImpersonation;

        protected NamedPipeClientBuilderBase(string pipeName, Type callbackContract = null, IServiceProvider serviceProvider = null) : base(callbackContract, serviceProvider) => _pipeName = pipeName;

        /// <summary>
        /// Don't set this if you can connect to less privileged processes. 
        /// Allow impersonation is false by default to prevent an escalation of privilege attack.
        /// If a privileged process connects to a less privileged one and the proxy allows impersonation then the server could impersonate the client's identity.
        /// </summary>
        /// <returns>this</returns>
        public TDerived AllowImpersonation()
        {
            _allowImpersonation = true;
            return this as TDerived;
        }

        protected override TInterface BuildCore(ServiceEndpoint serviceEndpoint) =>
            new NamedPipeClient<TInterface>(_serializer, _pipeName, _requestTimeout, _allowImpersonation, _logger, _connectionFactory, _beforeCall, serviceEndpoint).CreateProxy();
    }

    public class NamedPipeClientBuilder<TInterface> : NamedPipeClientBuilderBase<NamedPipeClientBuilder<TInterface>, TInterface> where TInterface : class
    {
        public NamedPipeClientBuilder(string pipeName) : base(pipeName) { }
    }

    public class NamedPipeClientBuilder<TInterface, TCallbackInterface> : NamedPipeClientBuilderBase<NamedPipeClientBuilder<TInterface, TCallbackInterface>, TInterface> where TInterface : class where TCallbackInterface : class
    {
        public NamedPipeClientBuilder(string pipeName, IServiceProvider serviceProvider) : base(pipeName, typeof(TCallbackInterface), serviceProvider) { }

        public NamedPipeClientBuilder<TInterface, TCallbackInterface> CallbackInstance(TCallbackInterface singleton)
        {
            _callbackInstance = singleton;
            return this;
        }

        public NamedPipeClientBuilder<TInterface, TCallbackInterface> TaskScheduler(TaskScheduler taskScheduler)
        {
            _taskScheduler = taskScheduler;
            return this;
        }
    }
}
