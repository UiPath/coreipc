using Newtonsoft.Json;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Channels;

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
    private static readonly Channel<RecordBase> Records = Channel.CreateUnbounded<RecordBase>();

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

            Task.Run(Pump).ContinueWith(task =>
            {
                Trace.TraceError(task.Exception?.ToString() ?? "Telemetry pump failed");
            }, TaskContinuationOptions.NotOnRanToCompletion);
        }

        static string ComputeLogFilePath()
        => Path.Combine(TelemetryFolder!, $"{CurrentProcessInfo.Name}-{CurrentProcessInfo.Id}.ndjson");
    }

    public static void Log(RecordBase record)
    {
        //if (record is ProcessStart { Name: "UiPath.Service.UserHost" })
        //{
        //    Debugger.Launch();
        //}

        if (!IsEnabled) { return; }
        _ = Records.Writer.TryWrite(record);
    }

    public static void Close()
    {
        _ = Records.Writer.TryComplete();
    }

    private static async Task Pump()
    {
        while (await Records.Reader.WaitToReadAsync())
        {
            try
            {
                var record = await Records.Reader.ReadAsync();
                var json = JsonConvert.SerializeObject(record, Jss);
                await Writer!.WriteLineAsync(json);
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
            }
        }

        Writer!.Close();
    }
}
