using System;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc
{
    public class EndpointSettings
    {
        internal static string Default { get; } = "Default";
        public EndpointSettings(Type contract, object serviceInstance = null, Type callbackContract = null)
        {
            Contract = contract ?? throw new ArgumentNullException(nameof(contract));
            Name = contract.Name;
            ServiceInstance = serviceInstance;
            CallbackContract = callbackContract;
            IOHelpers.Validate(contract);
        }
        internal string Name { get; private set; }
        internal TaskScheduler Scheduler { get; set; }
        internal object ServiceInstance { get; }
        internal Type Contract { get; }
        internal Type CallbackContract { get; }
        internal IServiceProvider ServiceProvider { get; set; }
        public bool IsDefault
        {
            get => Name == Default;
            set => Name = (value ? Default : Name);
        }
    }
    public class EndpointSettings<TContract> : EndpointSettings where TContract : class
    {
        public EndpointSettings(TContract serviceInstance = null, Type callbackContract = null) : base(typeof(TContract), serviceInstance, callbackContract) { }
    }
    public class EndpointSettings<TContract, TCallbackContract> : EndpointSettings<TContract> where TContract : class where TCallbackContract : class
    {
        public EndpointSettings(TContract serviceInstance = null) : base(serviceInstance, typeof(TCallbackContract)) { }
    }
}