using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UiPath.CoreIpc.Tests
{
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
