using System.Threading.Tasks;
using System.Threading;
using System;

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
    }
}
