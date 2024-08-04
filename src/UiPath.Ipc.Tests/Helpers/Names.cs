namespace UiPath.Ipc.Tests;

internal static class Names
{
    public const string GuiThreadName = "GuiThread";

    public static string GetPipeName(string role, TestRunId testRunId) => $"{role}_{testRunId.Value:N}";
}
