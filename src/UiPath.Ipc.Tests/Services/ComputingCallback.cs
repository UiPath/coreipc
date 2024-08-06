namespace UiPath.Ipc.Tests;

public sealed class ComputingCallback : IComputingCallback
{
    public Guid Id { get; } = Guid.NewGuid();
}