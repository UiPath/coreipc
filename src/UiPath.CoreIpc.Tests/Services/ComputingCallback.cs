namespace UiPath.Ipc.Tests;

public sealed class ComputingCallback : IComputingCallback
{
    public Guid Id { get; } = Guid.NewGuid();

    public async Task<string> GetThreadName() => Thread.CurrentThread.Name!;

    public async Task<int> AddInts(int x, int y) => x + y;

    public async Task<bool> DivideByZeroOnClient()
    {
        return await CallbackFrame1();
    }

    public async Task<bool> CallbackFrame1() => await CallbackFrame2();
    public async Task<bool> CallbackFrame2() => throw new DivideByZeroException();
}

