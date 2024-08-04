namespace UiPath.Ipc.Tests;

public readonly record struct TestRunId(Guid Value)
{
    public static TestRunId New() => new(Guid.NewGuid());
}
