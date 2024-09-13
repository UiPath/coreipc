using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Security.Cryptography.X509Certificates;

namespace UiPath.Ipc.TV;

public partial class FormMain : Form
{
    private readonly IServiceScopeFactory _scopeFactory;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FormProject? ActiveFormProjectForm => ActiveMdiChild as FormProject;

    public FormMain(IServiceScopeFactory scopeFactory)
    {
        InitializeComponent();
        _scopeFactory = scopeFactory;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
    }

    private void openToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (Telemetry.TelemetryFolder is { } notNull)
        {
            folderBrowserDialog.InitialDirectory = notNull;
        }

        if (folderBrowserDialog.ShowDialog() is not DialogResult.OK)
        {
            return;
        }

        OpenFolder(folderBrowserDialog.SelectedPath);
    }

    private void itemOpenStandard1_Click(object sender, EventArgs e)
    {
        if (Telemetry.TelemetryFolder is { } notNull)
        {
            OpenFolder(notNull);
            return;
        }

        MessageBox.Show("Environment variable not set.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void OpenFolder(string path)
    {
        var scope = _scopeFactory.CreateAsyncScope();
        var projectContext = scope.ServiceProvider.GetRequiredService<ProjectContext>();
        projectContext.ProjectPath = path;
        projectContext.DisposeScope = scope.DisposeAsync;

        var formRepo = scope.ServiceProvider.GetRequiredService<FormRepo>();
        formRepo.WindowState = FormWindowState.Maximized;
        formRepo.Show();

        //var formProject = scope.ServiceProvider.GetRequiredService<FormProject>();
        //formProject.WindowState = FormWindowState.Maximized;
        //formProject.Show();
    }

    private void Build(BuildTargets targets)
    {
        if (!BuildContext.TryCreate(targets, out var context))
        {
            return;
        }

        FormBuildAndDeploy.Execute(context);
    }

    private void buttonStartUiRobotSvc_Click(object sender, EventArgs e)
    => Build(BuildTargets.StartService);

    private void buttonStopUiRobotSvc_Click(object sender, EventArgs e)
    => Build(BuildTargets.StopService);

    private void buttonStartAssistant_Click(object sender, EventArgs e)
    => Build(BuildTargets.StartAssistant);

    private void buttonStopAssistant_Click(object sender, EventArgs e)
    => Build(BuildTargets.StopAssistant);

    private void buttonStartAll_Click(object sender, EventArgs e)
    => Build(BuildTargets.StopAll);

    private void buttonStopAll_Click_1(object sender, EventArgs e)
    => Build(BuildTargets.StopAll);

    private void buttonTheWorks_Click(object sender, EventArgs e)
    => Build(BuildTargets.Everything);

    private void FormMain_MdiChildActivate(object sender, EventArgs e)
    {
        toolStripProject.Enabled = ActiveFormProjectForm is not null;
    }

    private void buttonCreateFilter_Click(object sender, EventArgs e)
    {
        if (ActiveFormProjectForm is not { } form)
        {
            return;
        }

        form.ServiceProvider.GetRequiredService<FormFilter>().Show();
    }

    private void buttonOpen3_Click(object sender, EventArgs e)
    {
        if (Telemetry.TelemetryFolder is { } path)
        {
            OpenCallLog(path);
            return;
        }

        MessageBox.Show("Environment variable not set.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void OpenCallLog(string path)
    {
        var scope = _scopeFactory.CreateAsyncScope();
        var projectContext = scope.ServiceProvider.GetRequiredService<ProjectContext>();
        projectContext.ProjectPath = path;
        projectContext.DisposeScope = scope.DisposeAsync;

        var formProject = scope.ServiceProvider.GetRequiredService<FormProject>();
        formProject.WindowState = FormWindowState.Maximized;
        formProject.Show();
    }

    private void buttonOpenContext_Click(object sender, EventArgs e)
    {
        if (folderBrowserDialog.ShowDialog() is not DialogResult.OK)
        {
            return;
        }
        OpenFolder(folderBrowserDialog.SelectedPath);
    }

    private void buttonOpenCallLog_Click(object sender, EventArgs e)
    {
        if (folderBrowserDialog.ShowDialog() is not DialogResult.OK)
        {
            return;
        }
        OpenCallLog(folderBrowserDialog.SelectedPath);
    }

    private void toolStripButton1_Click(object sender, EventArgs e)
    => Build(BuildTargets.Build | BuildTargets.PurgeTelemetry);
}
