namespace UiPath.Ipc.Tests;

internal static class Timeouts
{
    public static readonly TimeSpan LocalProxyToThrowOCE = TimeSpan.FromMilliseconds(200);

    public static readonly TimeSpan IpcRoundtrip = TimeSpan.FromMilliseconds(800);

    public static readonly TimeSpan Short = TimeSpan.FromMilliseconds(300);

    public static readonly TimeSpan DefaultRequest = Debugger.IsAttached ? TimeSpan.FromDays(1) : TimeSpan.FromMinutes(1);
}
