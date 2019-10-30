using System;
using System.Threading;

namespace UiPath.CoreIpc
{
    public class EndpointSettings
    {
        public EndpointSettings(Type contract, object serviceInstance, Type callbackContract = null)
        {
            Contract = contract ?? throw new ArgumentNullException(nameof(contract));
            Name = contract.Name;
            ServiceInstance = serviceInstance;
            CallbackContract = callbackContract;
            IOHelpers.Validate(contract);
        }
        public TimeSpan RequestTimeout { get; set; } = Timeout.InfiniteTimeSpan;
        public byte ConcurrentAccepts { get; set; } = 5;
        public byte MaxReceivedMessageSizeInMegabytes { get; set; } = 2;
        public string Name { get; set; }
        internal object ServiceInstance { get; }
        internal Type Contract { get; }
        internal Type CallbackContract { get; }
    }
}