using System.Diagnostics.CodeAnalysis;

namespace UiPath.Ipc.TV;

public sealed class BuildContext
{
    public static bool TryCreate(BuildTargets targets, [NotNullWhen(returnValue: true)] out BuildContext? context)
    {
        const string EnvVar_CsProjPath = "UIPATH_COREIPC_CSPROJ_PATH";
        const string EnvVar_UserServicePath = "UIPATH_USER_SERVICE_PATH";
        const string EnvVar_TelemetryPath = "UIPATH_IPC_TELEMETRY_FOLDER";
        const string EnvVar_AssistantExe = "UIPATH_ASSISTANT_EXE";

        string? pathCsProj = null;
        string? pathUserService = null;
        string? pathTelemetry = null;
        string? pathAssistantExe = null;

        if (targets.Has(BuildTargets.Build))
        {
            if (!IsSet(EnvVar_CsProjPath, out pathCsProj) || !IsSet(EnvVar_UserServicePath, out pathUserService))
            {
                context = null;
                return false;
            }
        }

        if (targets.Has(BuildTargets.PurgeTelemetry))
        {
            if (!IsSet(EnvVar_TelemetryPath, out pathTelemetry))
            {
                context = null;
                return false;
            }
        }

        if (targets.Has(BuildTargets.StartAssistant))
        {
            if (!IsSet(EnvVar_AssistantExe, out pathAssistantExe))
            {
                context = null;
                return false;
            }
        }

        context = new()
        {
            Targets = targets,
            CsProj = ToFile(pathCsProj),
            UserService = ToFile(pathUserService!),
            TelemetryFolder = ToDirectory(pathTelemetry),
            AssistantExe = ToFile(pathAssistantExe),
        };
        return true;

        static bool IsSet(string envVarName, [NotNullWhen(returnValue: true)] out string? value)
        {
            value = EnvironmentPal.Get(envVarName);

            if (value is null)
            {
                MessageBox.Show($"Environment variable \"{envVarName}\" is not set.");
                return false;
            }

            return true;
        }

        [return: NotNullIfNotNull(nameof(path))]
        static FileInfo? ToFile(string? path)
        => path is null ? null : new(path);

        [return: NotNullIfNotNull(nameof(path))]
        static DirectoryInfo? ToDirectory(string? path)
        => path is null ? null : new(path);
    }

    public required BuildTargets Targets { get; init; }
    public FileInfo? CsProj { get; init; }
    public FileInfo? UserService { get; init; }
    public DirectoryInfo? TelemetryFolder { get; init; }
    public FileInfo? AssistantExe { get; init; }
}