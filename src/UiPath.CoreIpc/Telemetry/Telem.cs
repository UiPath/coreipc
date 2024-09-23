using Newtonsoft.Json;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace UiPath.Ipc;

public static partial class Telemetry
{
    public static readonly JsonSerializerSettings Jss = new()
    {
        TypeNameHandling = TypeNameHandling.All,
        TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Full,
        ReferenceLoopHandling = ReferenceLoopHandling.Serialize,
        PreserveReferencesHandling = PreserveReferencesHandling.Objects,
    };

    public const string EnvironmentVariableName = "UIPATH_IPC_TELEMETRY_FOLDER";
    public static readonly string? TelemetryFolder =
        Environment.GetEnvironmentVariable(EnvironmentVariableName) ??
        Environment.GetEnvironmentVariable(EnvironmentVariableName, EnvironmentVariableTarget.User) ??
        Environment.GetEnvironmentVariable(EnvironmentVariableName, EnvironmentVariableTarget.Machine);

    private static readonly StreamWriter? Writer;
    private static readonly object Lock = new();

    [MemberNotNullWhen(true, nameof(TelemetryFolder))]
    [MemberNotNullWhen(true, nameof(Writer))]
    public static bool IsEnabled
    => TelemetryFolder is not null &&
        CurrentProcessInfo.Name is not ("UiPath.Ipc.TV" or "testhost" or "testhost.x86" or "dotnet");

    static Telemetry()
    {
        if (IsEnabled)
        {
            var fileStream = new FileStream(ComputeLogFilePath(), FileMode.Create, FileAccess.Write, FileShare.Read);
            Writer = new StreamWriter(fileStream, Encoding.UTF8, 1024, leaveOpen: false) { AutoFlush = true };
        }

        static string ComputeLogFilePath()
        => Path.Combine(TelemetryFolder!, $"{CurrentProcessInfo.Name}-{CurrentProcessInfo.Id}.ndjson");
    }

    public static void Log(RecordBase record)
    {
        if (record is ILoggable loggable)
        {
            loggable.Logger?.Log(loggable.LogLevel, loggable.LogMessage);
        }

        //if (record is ProcessStart { Name: "UiPath.Service.UserHost" })
        //{
        //    Debugger.Launch();
        //}

        if (!IsEnabled) { return; }

        try
        {
            var json = JsonConvert.SerializeObject(record, Jss);
            lock (Lock)
            {
                Writer!.WriteLine(json);
            }
        }
        catch (Exception ex)
        {
            _ = new RecordSerializationException
            {
                RecordId = record.Id,
                RecordTypeName = record.GetType().AssemblyQualifiedName!,
                RecordToString = record.ToString(),
                ExceptionInfo = ex!,
            }.Log();
        }
    }

    public static void Close()
    {
        lock (Lock)
        {
            try
            {
                Writer?.Close();
            }
            catch
            {
                // ignore
            }
        }
    }
}
