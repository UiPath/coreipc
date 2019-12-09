using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UiPath.CoreIpc.Tests
{
    public interface ISystemCallback
    {
        Task<string> GetId(Message message = null);
    }
}
