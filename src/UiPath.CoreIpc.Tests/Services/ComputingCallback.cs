namespace UiPath.CoreIpc.Tests;

public sealed class ComputingCallback : IComputingCallback
{
    public Guid Id { get; } = Guid.NewGuid();

    public async Task<string> GetThreadName() => Thread.CurrentThread.Name!;

    public async Task<int> AddInts(int x, int y) => x + y;
}

