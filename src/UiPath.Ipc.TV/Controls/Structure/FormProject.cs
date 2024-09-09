using System.Diagnostics;

namespace UiPath.Ipc.TV;

public partial class FormProject : Form, IAsyncDisposable
{
    private bool _mayClose = false;

    public IServiceProvider ServiceProvider { get; }

    private readonly IProjectContext _projectContext;
    private readonly SlowDisposable _slowDisposable;
    private readonly FormProjectModel _model;

    private OrderedLineList _latestLineList = null!;

    public FormProject(FormMain mdiParent, IProjectContext projectContext, SlowDisposable slowDisposable, FormProjectModel model, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        MdiParent = mdiParent;
        _projectContext = projectContext;
        _slowDisposable = slowDisposable;
        _model = model;
        _model.State.Subscribe(ManifestProjectModelState);
        _model.RelationalModels.Subscribe(ManifestRelationalModel);

        Text = _projectContext.ProjectPath;
        ServiceProvider = serviceProvider;
    }

    private void ManifestRelationalModel(RelationalTelemetryModel model)
    {
        telemetryExplorer.Model = model;
    }

    private void listView1_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
    {
        e.Item = Create(_latestLineList.Lines[e.ItemIndex]);

        ListViewItem Create(Line line)
        {
            var item = new ListViewItem(line.Record.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            item.SubItems.Add(line.FileName);
            item.SubItems.Add(line.Record.GetType().Name);
            item.IndentCount = e.ItemIndex;
            return item;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_mayClose)
        {
            labelStatus.Text = "Disposing project scope...";
            e.Cancel = true;
            Pal().TraceError();
        }

        async Task Pal()
        {
            await Task.Yield();
            await _projectContext.DisposeScope();
            labelStatus.Text = "Project scope has been disposed.";
            _mayClose = true;
            Close();
        }
    }

    private void FormProject_Activated(object sender, EventArgs e)
    {
        _model.EnsureLoading();
    }

    private void ManifestProjectModelState(FormProjectModelState state)
    {
        labelStatus.Text = state.ToString();
        progressBar.Visible = state is FormProjectModelState.Reading;
    }

    private void buttonGoFind_Click(object sender, EventArgs e)
    {
        splitContainer1.Panel2Collapsed = false;
    }

    private void buttonScanOutgoing_Click(object sender, EventArgs e)
    {
        var results = FormProgress.ExecuteOnThreadPool(form =>
            OutgoingCallInfoBuilder.Build(
                telemetryExplorer.Model!,
                form
                    .Select<OutgoingCallInfoBuilder.ProgressReport, (string? label, int cTotal, int cProcessed)>(Translate)
                    .ScheduleOn(TaskScheduler.FromCurrentSynchronizationContext()),
                form._cts.Token));

        watchView1.Model = new ValueSource.Variable("calls", typeof(OutgoingCallInfoResults), results);
        static (string? label, int cTotal, int cProcessed) Translate(OutgoingCallInfoBuilder.ProgressReport report)
        {
            return (null, report.CTotal, report.CProcessed);
        };
    }

    ValueTask IAsyncDisposable.DisposeAsync() => _projectContext.DisposeAsync();
}

public class SlowDisposable : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await Task.Delay(100);
    }
}