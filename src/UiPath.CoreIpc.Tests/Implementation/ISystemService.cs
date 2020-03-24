using System;
using System.Threading;
using System.Threading.Tasks;
using UiPath.CoreIpc;

namespace UiPath.CoreIpc.Tests
{
    public interface ISystemService
    {
        Task<OneWay> DoNothing(CancellationToken cancellationToken = default);
        Task<OneWay> VoidThreadName(CancellationToken cancellationToken = default);
        Task<OneWay> VoidSyncThrow(CancellationToken cancellationToken = default);
        Task<string> GetThreadName(CancellationToken cancellationToken = default);
        Task<string> ConvertText(string text, TextStyle style, CancellationToken cancellationToken = default);
        Task<Guid> GetGuid(Guid guid, CancellationToken cancellationToken = default);
        Task<byte[]> ReverseBytes(byte[] input, CancellationToken cancellationToken = default);
        Task SlowOperation(CancellationToken cancellationToken = default);
        Task<string> MissingCallback(SystemMessage message, CancellationToken cancellationToken = default);
        Task Infinite(CancellationToken cancellationToken = default);
        Task<string> ImpersonateCaller(Message message = null, CancellationToken cancellationToken = default);
        Task<string> SendMessage(SystemMessage message, CancellationToken cancellationToken = default);
    }

    public class SystemMessage : Message
    {
        public string Text { get; set; }
        public int Delay { get; set; }
    }
}
