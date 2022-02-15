using System.Threading.Tasks;
using System.Threading;
using System;
using System.Diagnostics;

namespace UiPath.CoreIpc.NodeInterop
{
    using static Contracts;

    internal static class ServiceImpls
    {
        public sealed class Algebra : IAlgebra
        {
            public Task<string> Ping() => Task.FromResult("Pong");

            public Task<int> MultiplySimple(int x, int y) => Task.FromResult(x * y);

            public async Task<int> Multiply(int x, int y, Message message = default)
            {
                var arithmetics = message.GetCallback<IArithmetics>();

                int result = 0;
                for (int i = 0; i < x; i++)
                {
                    result = await arithmetics.Sum(result, y);
                }

                return result;
            }

            public async Task<bool> Sleep(int milliseconds, Message message = default, CancellationToken ct = default)
            {
                await Task.Delay(milliseconds, ct);
                return true;
            }

            public Task<bool> Timeout() => Task.FromException<bool>(new TimeoutException());

            public Task<int> Echo(int x) => Task.FromResult(x);
        }

        public class Calculus : ICalculus
        {
            public Task<string> Ping() => Task.FromResult("Pong");
        }

        public sealed class BrittleService : IBrittleService
        {
            public async Task<int> Sum(int x, int y, TimeSpan delay, DateTime? crashBeforeUtc)
            {
                if (DateTime.UtcNow < crashBeforeUtc)
                {
                    Console.WriteLine("Exiting on purpose...");
                    Process.GetCurrentProcess().Kill();
                }

                await Task.Delay(delay);
                return x + y;
            }

            public Task Kill()
            {
                Process.GetCurrentProcess().Kill();
                throw null!; // making the compiler happy
            }
        }

        public sealed class EnvironmentVariableGetter : IEnvironmentVariableGetter
        {
            public Task<string?> Get(string variable) => Task.FromResult<string?>(Environment.GetEnvironmentVariable(variable));
        }

        public class Dto : IDto
        {
            public Dto ReturnDto(Dto myDto) => myDto;
        }
    }
}
