using System;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.CoreIpc.NodeInterop;

internal static class Contracts
{
    public interface IArithmetic
    {
        Task<int> Sum(int x, int y);
        Task<bool> SendMessage(Message<int> message);
    }

    public interface IAlgebra
    {
        Task<string> Ping();
        Task<int> MultiplySimple(int x, int y);
        Task<int> Multiply(int x, int y, Message message = default!);
        Task<bool> Sleep(int milliseconds, Message message = default!, CancellationToken ct = default);
        Task<bool> Timeout();
        Task<int> Echo(int x);
        Task<bool> TestMessage(Message<int> message);
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

    public interface IDtoService
    {
        Task<Dto> ReturnDto(Dto dto);
    }
    public class Dto
    {
        public bool BoolProperty { get; set; }
        public int IntProperty { get; set; }
        public string StringProperty { get; set; }

        public Dto(bool boolProperty, int intProperty, string stringProperty)
        {
            BoolProperty = boolProperty;
            IntProperty = intProperty;
            StringProperty = stringProperty;
        }
    }

}
