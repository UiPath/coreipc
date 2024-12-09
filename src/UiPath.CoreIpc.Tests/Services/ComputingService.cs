using Microsoft.Extensions.Logging;

namespace UiPath.CoreIpc.Tests;

public sealed class ComputingService(ILogger<ComputingService> logger) : IComputingService
{
    private static readonly AsyncLocal<string?> ContextStorage = new();
    public static string? Context
    {
        get => ContextStorage.Value;
        set => ContextStorage.Value = value;
    }

    public async Task<float> AddFloats(float a, float b, CancellationToken ct = default)
    {
        logger.LogInformation($"{nameof(AddFloats)} called.");
        return a + b;
    }

    public async Task<ComplexNumber> AddComplexNumbers(ComplexNumber a, ComplexNumber b, CancellationToken ct = default)
    {
        logger.LogInformation($"{nameof(AddComplexNumbers)} called.");
        return a + b;
    }
    public async Task<ComplexNumber> AddComplexNumberList(IReadOnlyList<ComplexNumber> numbers, CancellationToken ct)
    {
        var result = ComplexNumber.Zero;
        foreach (var number in numbers)
        {
            result += number;
        }
        return result;
    }

    public async Task<bool> Wait(TimeSpan duration, CancellationToken ct = default)
    {
        await Task.Delay(duration, ct);
        return true;
    }

    public async Task<string> GetCallbackThreadName(TimeSpan waitOnServer, Message message = null!, CancellationToken cancellationToken = default)
    {
        await Task.Delay(waitOnServer);
        return await message.Client.GetCallback<IComputingCallback>().GetThreadName();
    }

    public async Task<int> MultiplyInts(int x, int y, Message message = null!)
    {
        var callback = message.Client.GetCallback<IComputingCallbackBase>();

        var result = 0;
        for (int i = 0; i < y; i++)
        {
            result = await callback.AddInts(result, x);
        }

        return result;
    }

    public async Task<string?> GetCallContext()
    {
        await Task.Delay(1).ConfigureAwait(continueOnCapturedContext: false);
        return Context;
    }

    public async Task<string> SendMessage(Message m = null!, CancellationToken ct = default)
    {
        return await m.Client.GetCallback<IComputingCallback>().GetThreadName();
    }
}
