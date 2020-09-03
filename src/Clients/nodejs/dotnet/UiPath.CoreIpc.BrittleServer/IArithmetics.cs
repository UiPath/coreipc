using System;
using System.Threading.Tasks;

namespace UiPath.CoreIpc.SampleServer
{
    public interface IArithmetics
    {
        Task<int> Sum(int x, int y, TimeSpan delay, DateTime failBeforeUtc);
    }
}
