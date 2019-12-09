using System;
using System.Threading;
using System.Threading.Tasks;
using UiPath.CoreIpc;

namespace UiPath.CoreIpc.Tests
{
    public interface ISystemService
    {
        Task DoNothing(CancellationToken cancellationToken = default);
        Task VoidThreadName(CancellationToken cancellationToken = default);
        Task VoidSyncThrow(CancellationToken cancellationToken = default);
        Task<string> GetThreadName(CancellationToken cancellationToken = default);
        Task<string> ConvertText(string text, TextStyle style, CancellationToken cancellationToken = default);
        Task<Guid> GetGuid(Guid guid, CancellationToken cancellationToken = default);
        Task<byte[]> ReverseBytes(byte[] input, CancellationToken cancellationToken = default);
        Task<bool> SlowOperation(CancellationToken cancellationToken = default);
        Task<string> MissingCallback(SystemMessage message, CancellationToken cancellationToken = default);
        Task<bool> Infinite(CancellationToken cancellationToken = default);
        Task<string> ImpersonateCaller(Message message = null, CancellationToken cancellationToken = default);
        Task<string> SendMessage(SystemMessage message, CancellationToken cancellationToken = default);
    }

    public class SystemMessage : Message
    {
        public string Text { get; set; }
        public int Delay { get; set; }
    }
}
