using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UiPath.Ipc.Tests
{
    public interface IInvalid : IDisposable
    {
    }

    public interface IDuplicateMessage
    {
        Task Test(Message message1, Message message2);
    }

    public interface IMessageFirst
    {
        Task Test(Message message1, int x);
    }

    public interface IInvalidCancellationToken
    {
        Task Test(CancellationToken token, int x);
    }

    public interface IComputingServiceBase
    {
        Task<float> AddFloat(float x, float y, CancellationToken cancellationToken = default);
    }
    public interface IComputingService : IComputingServiceBase
    {
        Task<ComplexNumber> AddComplexNumber(ComplexNumber x, ComplexNumber y, CancellationToken cancellationToken = default);
        Task<ComplexNumber> AddComplexNumbers(IEnumerable<ComplexNumber> numbers, CancellationToken cancellationToken = default);
        Task<string> SendMessage(SystemMessage message, CancellationToken cancellationToken = default);
        Task<bool> Infinite(CancellationToken cancellationToken = default);
        Task InfiniteVoid(CancellationToken cancellationToken = default);
        Task<string> GetCallbackThreadName(Message message, CancellationToken cancellationToken = default);
    }

    public struct ComplexNumber
    {
        public float A { get; set; }
        public float B { get; set; }

        public ComplexNumber(float a, float b)
        {
            A = a;
            B = b;
        }
    }

    public enum TextStyle
    {
        TitleCase,
        Upper
    }
}
