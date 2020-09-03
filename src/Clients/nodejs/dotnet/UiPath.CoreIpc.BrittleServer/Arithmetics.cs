using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace UiPath.CoreIpc.SampleServer
{
    public sealed class Arithmetics : IArithmetics
    {
        public async Task<int> Sum(int x, int y, TimeSpan delay, DateTime crashBeforeUtc)
        {
            if (DateTime.UtcNow < crashBeforeUtc)
            {
                Console.WriteLine("Exiting on purpose...");
                Process.GetCurrentProcess().Kill();
            }

            await Task.Delay(delay);
            return x + y;
        }
    }
}
