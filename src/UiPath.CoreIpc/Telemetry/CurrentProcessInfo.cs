namespace UiPath.Ipc;

public class CurrentProcessInfo
{
    public static readonly string Name;
    public static readonly int Id;
    public static readonly string Path;
    public static readonly string CommandLine;

    static CurrentProcessInfo()
    {
        using var self = Process.GetCurrentProcess();
        using var mainModule = self.MainModule!;

        Name = self.ProcessName;
        Id = self.Id;
        Path = mainModule.FileName!;
        CommandLine = Environment.CommandLine;
    }
}
