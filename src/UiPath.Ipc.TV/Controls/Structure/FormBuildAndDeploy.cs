using Nito.Disposables;
using System.Diagnostics;
using System.ServiceProcess;

namespace UiPath.Ipc.TV;

public partial class FormBuildAndDeploy : Form
{
    public static void Execute(BuildContext context)
    {
        var instance = new FormBuildAndDeploy(context);
        instance.ShowDialog();
    }

    private const string ServiceName = "UiRobotSvc";
    private readonly CancellationTokenSource _cts = new();
    private bool _mayClose = false;

    private readonly BuildContext _context;

    public FormBuildAndDeploy(BuildContext context)
    {
        InitializeComponent();        
        _context = context;
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Dispose();
    }

    private void FormBuildAndDeploy_Load(object sender, EventArgs e)
    {
        ExecuteSteps().TraceError();
    }

    private async Task ExecuteSteps(CancellationToken ct = default)
    {
        buttonCancel.Enabled = true;
        buttonOk.Enabled = false;

        try
        {
            await MaybeBuild(ct);
            var didStop = await MaybeStopService(ct);
            didStop = true; // always start service

            await MaybeStopAssistant(ct);
            await MaybeStopOtherProcesses(ct);
            await MaybeDeploy(ct);
            await MaybePurgeTelemetry(ct);
            await MaybeRestartService(didStop, ct);
            await MaybeRestartAssistant(ct);

            buttonOk.Enabled = true;
            buttonCancel.Enabled = false;
            CancelButton = buttonOk;

            WriteLine(null);
            WriteLine(null);
            AppendHeader("All done. You can close this window.");
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == ct)
        {
            WriteLine(null);
            WriteLine(null);
            WriteLineError("Process was cancelled. You can close this window.");

            buttonCancel.Text = "Close";
            buttonCancel.Enabled = true;
            buttonCancel.DialogResult = DialogResult.Cancel;
            buttonCancel.Click -= buttonCancel_Click!;
        }
        finally
        {
            _mayClose = true;
        }
    }

    private async Task MaybePurgeTelemetry(CancellationToken ct)
    {
        WriteLine(null);
        AppendHeader("Potentially purging telemetry...");

        if (!_context.Targets.Has(BuildTargets.PurgeTelemetry))
        {
            WriteLine("Purging was not requested.");
            return;
        }

        await Task.Run(() =>
        {
            var files = _context.TelemetryFolder!.GetFiles();
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                WriteLine($"Deleting file \"{file.FullName}\"...");
                File.Delete(file.FullName);
            }
        }, ct);
    }

    private async Task MaybeBuild(CancellationToken ct)
    {
        string? fileName = _context.CsProj?.Name;
        WriteLine(null);
        AppendHeader($"Potentially building...");

        if (!_context.Targets.Has(BuildTargets.Build))
        {
            WriteLine("Build was not requested. Skipping step...");
            return;
        }

        await ProcessPal.Run(
            startInfo: new()
            {
                FileName = "dotnet",
                Arguments = $"build {fileName}",
                WorkingDirectory = _context.CsProj!.DirectoryName,
            },
            scheduler: TaskScheduler.FromCurrentSynchronizationContext(),
            stdout: new Progress<string?>(WriteLine),
            stderr: new Progress<string?>(WriteLineError),
            ct: ct);
    }
    private async Task<bool> MaybeStopService(CancellationToken ct)
    {
        WriteLine(null);
        AppendHeader($"Potentially stopping service \"{ServiceName}\"...");

        if (!_context.Targets.Has(BuildTargets.StopAll))
        {
            WriteLine("Stopping was not requested. Skipping step...");
            return false;
        }

        WriteLine("Finding service...");
        if (TryFindService(ServiceName) is not { } sc)
        {
            MessageBox.Show($"Service \"{ServiceName}\" not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        if (sc.Status is ServiceControllerStatus.Stopped)
        {
            WriteLine("Service already stopped.");
            return false;
        }

        WriteLine("Stopping service...");
        await Task.Run(() =>
        {
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }, ct);
        WriteLine("Service stopped.");

        return true;
    }

    private Task MaybeStopOtherProcesses(CancellationToken ct)
    => MaybeStopProcesses(IsOtherUiPathProcess, "other UiPath processes", ct);

    private Task MaybeStopAssistant(CancellationToken ct)
    => MaybeStopProcesses(IsAssistant, "UiPath Assistant", ct);

    private async Task MaybeStopProcesses(Func<Process, bool> predicate, string title, CancellationToken ct)
    {
        WriteLine(null);
        AppendHeader($"Potentially stopping {title}...");

        if (!_context.Targets.Has(BuildTargets.StopAll))
        {
            WriteLine("Stopping was not requested. Skipping step...");
            return;
        }

        await Task.Run(() =>
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                WriteLine($"(Re)checking {title}...");
                var processes = Process.GetProcesses();
                using var _ = new CollectionDisposable(processes);

                var found = processes.Where(predicate).ToArray();
                if (found is [])
                {
                    return;
                }

                foreach (var process in found)
                {
                    ct.ThrowIfCancellationRequested();
                    WriteLine($"Killing process \"{process.ProcessName}\"...");
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (Exception ex)
                    {
                        WriteLineError(ex.ToString());
                    }
                }
            }
        });

    }

    private async Task MaybeRestartAssistant(CancellationToken ct)
    {
        AppendHeader("Potentially starting UiPath Assistant");

        if (!_context.Targets.Has(BuildTargets.StartAssistant))
        {
            WriteLine("Starting UiPath Assistant was not requested. Skipping step...");
            return;
        }

        var pid = await MaybeStartProgram(_context.AssistantExe!, ct);
        Write("Started UiPath Assistant with PID=", Color.Gray);
        WriteLine($"{pid}", Color.White);
    }

    private async Task<int> MaybeStartProgram(FileInfo exe, CancellationToken ct)
    { 
        using var process = new Process
        {
            StartInfo =
            {
                FileName = exe.FullName,
                WorkingDirectory = exe.DirectoryName,
            }
        };
        _ = process.Start();
        return process.Id;
    }


    private static bool IsOtherUiPathProcess(Process process) =>
        process.Id != CurrentProcessInfo.Id &&
        !process.ProcessName.Equals("UiPath.Assistant", StringComparison.OrdinalIgnoreCase) &&
        (process.ProcessName.Equals("UiRobot", StringComparison.OrdinalIgnoreCase) ||
         process.ProcessName.StartsWith("UiPath.", StringComparison.OrdinalIgnoreCase));
    private static bool IsAssistant(Process process) => process.ProcessName.Equals("UiPath.Assistant", StringComparison.OrdinalIgnoreCase);

    private async Task MaybeDeploy(CancellationToken ct)
    {
        WriteLine(null);
        AppendHeader("Potentially deploying UiPath.Ipc...");

        if (!_context.Targets.Has(BuildTargets.Build))
        {
            WriteLine("Deployment was not requested. Skipping step...");
            return;
        }

        var pathDestinationDir = _context.UserService!.DirectoryName;
        var pathSourceDir = Path.GetFullPath(Path.Combine(_context.CsProj!.DirectoryName!, "bin", "Debug", "net6.0-windows"));

        WriteLine($"Copying files from \"{pathSourceDir}\" to \"{pathDestinationDir}\"...");
        await Task.Run(() =>
        {
            foreach (var file in Directory.EnumerateFiles(pathSourceDir))
            {
                ct.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(file);
                var pathDestinationFile = Path.Combine(pathDestinationDir, fileName);

                Write($"Copying \"", Color.Gray, indent: true);
                Write(fileName, Color.White);
                Write("\"...", Color.Gray);
                File.Copy(file, pathDestinationFile, overwrite: true);
                WriteLine(" DONE!", Color.Lime);
            }
        });
    }
    private async Task MaybeRestartService(bool didStop, CancellationToken ct)
    {
        WriteLine(null);
        AppendHeader($"Potentially restarting the \"{ServiceName}\" service...");

        if (!_context.Targets.Has(BuildTargets.StartService))
        {
            WriteLine("Neither StartService nor Build were not requested. Skipping step...");
            return;
        }

        if (!didStop && _context.Targets.Has(BuildTargets.Build))
        {
            WriteLine("Service had already been stopped. Skipping start...");
            return;
        }

        WriteLine("Starting service...");
        if (TryFindService(ServiceName) is not { } sc)
        {
            MessageBox.Show($"Service \"{ServiceName}\" not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        await Task.Run(() =>
        {
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
        }, ct);
        WriteLine("Service started.");
    }

    private ServiceController? TryFindService(string serviceName)
    {
        var services = ServiceController.GetServices() as ServiceController?[];
        var index = Array.FindIndex(services, candidate => candidate!.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase));

        ServiceController? result = null;
        if (index > -1)
        {
            result = services[index];
            services[index] = null;
        }

        using (new CollectionDisposable(services))
        {
            return result;
        }
    }


    // Appends text to the rich text box with the color gray
    private void WriteLine(string? text) => WriteLine(text, Color.Gray);
    private void WriteLineError(string? text) => WriteLine(text, Color.Red);

    private void AppendHeader(string? text)
    {
        const int HeaderLength = 80;
        int leftTildeCount = (HeaderLength - ((text?.Length + 2) ?? 0)) / 2;
        int rightTildeCount = HeaderLength - ((text?.Length + 2) ?? 0) - leftTildeCount;
        
        Write(new string('~', leftTildeCount), Color.Gray, indent: false);
        Write(text is null ? "" : $" {text} ", Color.White, indent: false);
        WriteLine(new string('~', rightTildeCount), Color.Gray, indent: false);
    }

    private void WriteLine(string? text, Color color, bool indent = true) => Write($"{text}{Environment.NewLine}", color, indent);

    private void Write(string text, Color color, bool indent = false)
    {
        if (InvokeRequired)
        {
            Invoke(() => Write(text, color, indent));
            return;
        }

        richTextBox.SelectionColor = color;
        if (indent)
        {
            richTextBox.AppendText("    ");
        }
        richTextBox.AppendText(text);
    }

    private void buttonCancel_Click(object sender, EventArgs e)
    {
        MaybeTriggerClose();
    }

    private void FormBuildAndDeploy_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (!_mayClose)
        {
            MaybeTriggerClose();
        }
    }

    private void MaybeTriggerClose()
    {
        if (MessageBox.Show("Are you sure you want to cancel this process?", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) is not DialogResult.Yes)
        {
            return;
        }

        buttonCancel.Enabled = false;
        buttonCancel.Text = "Canceling...";
        _cts.Cancel();
    }
}
