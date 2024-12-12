namespace UiPath.Ipc.Tests;

public sealed class ArithmeticCallback : IArithmeticCallback
{
    public async Task<int> Increment(int x) => x + 1;
}

