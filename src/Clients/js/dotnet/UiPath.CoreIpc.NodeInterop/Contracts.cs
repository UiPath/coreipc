using System;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.Ipc.NodeInterop;

internal static class Contracts
{
    public interface IArithmetic
    {
        Task<int> Sum(int x, int y);
        Task<bool> SendMessage(Message<int> message);
    }

    // A callback the server invokes then cancels; the client handler must observe it
    // (as a CancellationToken or, interchangeably, an AbortSignal).
    public interface ICancellationCallback
    {
        Task<bool> Wait(CancellationToken ct = default);
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
        // Blocks until its (injected) CancellationToken fires, then throws — used
        // to prove a client actually transmits a cancellation to the server.
        Task<bool> WaitForCancellation(CancellationToken ct = default);
        // How many times WaitForCancellation has observed a cancellation so far.
        Task<int> CancellationCount();
        // Invokes ICancellationCallback.Wait on the client, then cancels it. Returns true
        // once the client-hosted handler observed the server-initiated cancel.
        Task<bool> CancelCallback(Message message = default!);
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
