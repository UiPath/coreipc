using Shouldly;
using System.Threading.Tasks;

namespace UiPath.CoreIpc.Tests
{
    public interface ISystemCallback
    {
        Task<string> GetId(Message message = null);
    }
    public class SystemCallback : ISystemCallback
    {
        public string Id { get; set; }
        public async Task<string> GetId(Message message)
        {
            message.Client.ShouldBeNull();
            return Id;
        }
    }
}