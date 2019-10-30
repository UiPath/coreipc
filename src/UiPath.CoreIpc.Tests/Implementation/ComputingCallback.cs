using Shouldly;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc.Tests
{
    public class ComputingCallback : IComputingCallback
    {
        public string Id { get; set; }
        public async Task<string> GetId(Message message)
        {
            message.Client.ShouldBeNull();
            return Id;
        }

        public async Task<string> GetThreadName() => Thread.CurrentThread.Name;
    }
}