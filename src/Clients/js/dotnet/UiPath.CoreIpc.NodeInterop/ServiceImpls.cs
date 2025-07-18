using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.Ipc.NodeInterop;

using static Contracts;

internal static class ServiceImpls
{
    public sealed class Algebra : IAlgebra
    {
        public Task<string> Ping() => Task.FromResult("Pong");

        public async Task<int> MultiplySimple(int x, int y)
        {
            return x * y;
        }

        public async Task<int> Multiply(int x, int y, Message message = default!)
        {
            var arithmetic = message.Client.GetCallback<IArithmetic>();

            int result = 0;
            for (int i = 0; i < x; i++)
            {
                result = await arithmetic.Sum(result, y);
            }

            return result;
        }
        public async Task<bool> TestMessage(Message<int> message)
        {
            var arithmetic = message.Client.GetCallback<IArithmetic>();
            return await arithmetic.SendMessage(message);
        }

        public async Task<bool> Sleep(int milliseconds, Message message = default!, CancellationToken ct = default)
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

    public sealed class DtoService : IDtoService
    {
        public Task<Dto> ReturnDto(Dto myDto)
        {
            return Task.FromResult(myDto);
        }
    }
}
