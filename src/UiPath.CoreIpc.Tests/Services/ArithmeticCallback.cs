namespace UiPath.CoreIpc.Tests;

public sealed class ArithmeticCallback : IArithmeticCallback
{
    public async Task<int> Increment(int x) => x + 1;
}

