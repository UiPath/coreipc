
namespace UiPath.Ipc.Tests;

public interface IComputingServiceBase
{
    Task<float> AddFloats(float x, float y, CancellationToken ct = default);
}

public interface IComputingService : IComputingServiceBase
{
    Task<ComplexNumber> AddComplexNumbers(ComplexNumber a, ComplexNumber b, CancellationToken ct = default);    
    Task<ComplexNumber> AddComplexNumberList(IReadOnlyList<ComplexNumber> numbers, CancellationToken ct = default);
    Task<bool> Wait(TimeSpan duration, CancellationToken ct = default);
    Task<string> GetCallbackThreadName(TimeSpan waitOnServer, Message message = null!, CancellationToken cancellationToken = default);
    Task<int> MultiplyInts(int x, int y, Message message = null!);
    Task<string?> GetCallContext();
    Task<string> SendMessage(Message m = null!, CancellationToken ct = default);
}

public interface IComputingCallbackBase
{
    Task<int> AddInts(int x, int y);
}

public interface IComputingCallback : IComputingCallbackBase
{
    Task<string> GetThreadName();
}

public interface IArithmeticCallback
{
    Task<int> Increment(int x);
}

public readonly record struct ComplexNumber
{
    public static readonly ComplexNumber Zero = default;
    public static ComplexNumber operator +(ComplexNumber a, ComplexNumber b)
    => new()
    {
        I = a.I + b.I,
        J = a.J + b.J
    };

    public required float I { get; init; }
    public required float J { get; init; }

    public override string ToString() => $"[{I}, {J}]";
}
