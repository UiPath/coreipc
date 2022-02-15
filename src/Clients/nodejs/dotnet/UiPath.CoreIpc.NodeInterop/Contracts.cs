using System.Threading.Tasks;
using System.Threading;
using System;
using static UiPath.CoreIpc.NodeInterop.ServiceImpls;

namespace UiPath.CoreIpc.NodeInterop
{
    internal static class Contracts
    {
        public interface IArithmetics
        {
            Task<int> Sum(int x, int y);
        }

        public interface IAlgebra
        {
            Task<string> Ping();
            Task<int> MultiplySimple(int x, int y);
            Task<int> Multiply(int x, int y, Message message = default);
            Task<bool> Sleep(int milliseconds, Message message = default, CancellationToken ct = default);
            Task<bool> Timeout();
            Task<int> Echo(int x);
        }

        public interface ICalculus
        {
            Task<string> Ping();
        }

        public interface IBrittleService
        {
            Task<int> Sum(int x, int y, TimeSpan delay, DateTime? crashBeforeUtc);

            Task Kill();
        }

        public interface IEnvironmentVariableGetter
        {
            Task<string?> Get(string variable);
        }

        public interface IDto
        {
            public Dto ReturnDto(Dto myDto);
        }
    }
}
