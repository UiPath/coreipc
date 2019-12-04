﻿using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace UiPath.CoreIpc.NamedPipe
{
    public abstract class NamedPipeClientBuilderBase<TDerived, TInterface> : ServiceClientBuilder<TDerived, TInterface> where TInterface : class where TDerived : ServiceClientBuilder<TDerived, TInterface>
    {
        private string _pipeName = typeof(TInterface).Name;
        private string _serverName = ".";
        private bool _allowImpersonation;

        protected NamedPipeClientBuilderBase(Type callbackContract = null, IServiceProvider serviceProvider = null) : base(callbackContract, serviceProvider) { }

        public TDerived PipeName(string pipeName)
        {
            _pipeName = pipeName;
            return this as TDerived;
        }

        public TDerived ServerName(string serverName)
        {
            _serverName = serverName;
            return this as TDerived;
        }

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

        protected override TInterface BuildCore(EndpointSettings serviceEndpoint) =>
            new NamedPipeClient<TInterface>(_serverName, _pipeName, _serializer, _requestTimeout, _allowImpersonation, _logger, _connectionFactory, _encryptAndSign, _beforeCall, serviceEndpoint).CreateProxy();
    }

    public class NamedPipeClientBuilder<TInterface> : NamedPipeClientBuilderBase<NamedPipeClientBuilder<TInterface>, TInterface> where TInterface : class
    {
    }

    public class NamedPipeClientBuilder<TInterface, TCallbackInterface> : NamedPipeClientBuilderBase<NamedPipeClientBuilder<TInterface, TCallbackInterface>, TInterface> where TInterface : class where TCallbackInterface : class
    {
        public NamedPipeClientBuilder(IServiceProvider serviceProvider) : base(typeof(TCallbackInterface), serviceProvider) { }

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
