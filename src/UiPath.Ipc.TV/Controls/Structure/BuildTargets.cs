namespace UiPath.Ipc.TV;

[Flags]
public enum BuildTargets
{
    StopService = 1,
    StopAssistant = 2,
    StopOthers = 4,
    StopAll = StopService | StopAssistant | StopOthers,

    StartService = 8,
    StartAssistant = 16,
    StartBoth = StartService | StartAssistant,

    JustBuild = 32,
    Build = StopAll | StartService | JustBuild,

    PurgeTelemetry = 64,
    Everything = Build | PurgeTelemetry | StartAssistant,
}

public static class BuildTargetsExtensions
{
    public static bool Has(this BuildTargets haystack, BuildTargets needle)
    => (haystack & needle) == needle;
}


