using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace UiPath.CoreIpc.Tests
{
    public interface IComputingCallback
    {
        Task<string> GetId(Message message);
        Task<string> GetThreadName();
    }
}