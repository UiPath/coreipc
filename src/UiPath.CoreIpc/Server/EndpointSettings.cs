using System;
using System.Threading;

namespace UiPath.CoreIpc
{
    public class EndpointSettings
    {
        public EndpointSettings(string name, Type contract, object serviceInstance, Type callbackContract = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Contract = contract ?? throw new ArgumentNullException(nameof(contract));
            ServiceInstance = serviceInstance;
            CallbackContract = callbackContract;
            IOHelpers.Validate(contract);
        }
        public TimeSpan RequestTimeout { get; set; } = Timeout.InfiniteTimeSpan;
        public byte ConcurrentAccepts { get; set; } = 5;
        public byte MaxReceivedMessageSizeInMegabytes { get; set; } = 2;
        internal string Name { get; }
        internal object ServiceInstance { get; }
        internal Type Contract { get; }
        internal Type CallbackContract { get; }
    }
}