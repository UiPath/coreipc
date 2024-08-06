
namespace UiPath.Ipc.Tests;

public interface IComputingService
{
    Task<float> AddFloats(float x, float y, CancellationToken ct = default);
    Task<ComplexNumber> AddComplexNumbers(ComplexNumber a, ComplexNumber b);
    Task<bool> Wait(TimeSpan duration, CancellationToken ct = default);
    Task<string> GetCallbackThreadName(TimeSpan duration, Message message = null!, CancellationToken cancellationToken = default);
    Task<ComplexNumber> AddComplexNumberList(IReadOnlyList<ComplexNumber> numbers);
    Task<int> MultiplyInts(int x, int y, Message message = null!);
}

public interface IComputingCallback
{
    Task<string> GetThreadName();
    Task<int> AddInts(int x, int y);
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