using Microsoft.Extensions.Logging;

namespace UiPath.Ipc.Tests;

public sealed class ComputingService(ILogger<ComputingService> logger) : IComputingService
{
    public static string? CallContext{ get; set; }

    public async Task<float> AddFloats(float a, float b, CancellationToken ct = default)
    {
        logger.LogInformation($"{nameof(AddFloats)} called.");
        return a + b;
    }

    public async Task<ComplexNumber> AddComplexNumbers(ComplexNumber a, ComplexNumber b)
    {
        logger.LogInformation($"{nameof(AddComplexNumbers)} called.");
        return a + b;
    }

    public async Task<bool> Wait(TimeSpan duration, CancellationToken ct = default)
    {
        await Task.Delay(duration, ct);
        return true;
    }

    public async Task<string> GetCallbackThreadName(TimeSpan waitOnServer, Message message = null!, CancellationToken cancellationToken = default)
    {
        await Task.Delay(waitOnServer);
        return await message.GetCallback<IComputingCallback>().GetThreadName();
    }

    public async Task<ComplexNumber> AddComplexNumberList(IReadOnlyList<ComplexNumber> numbers)
    {
        var result = ComplexNumber.Zero;
        foreach (var number in numbers)
        {
            result += number;
        }
        return result;
    }

    public async Task<int> MultiplyInts(int x, int y, Message message = null!)
    {
        var callback = message.GetCallback<IComputingCallbackBase>();

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
        return CallContext;
    }
}
