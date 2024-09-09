namespace UiPath.Ipc.TV;

internal static class EnvironmentPal
{
    public static string? Get(string name) 
    => Environment.GetEnvironmentVariable(name)
        ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
        ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);
}
