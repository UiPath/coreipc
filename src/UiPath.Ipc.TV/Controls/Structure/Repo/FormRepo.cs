namespace UiPath.Ipc.TV;

public partial class FormRepo : Form
{
    private readonly DirectoryInfo _dir;
    private readonly CancellationTokenSource _ctsIndexing = new();

    private RecordRepo _repo = null!;

    public FormRepo(FormMain mdiParent, IProjectContext projectContext)
    {
        InitializeComponent();
        MdiParent = mdiParent;
        _dir = new(projectContext.ProjectPath);
        panelIndexing.BringToFront();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _ctsIndexing.Cancel();
        _ctsIndexing.Dispose();
    }

    protected override void OnLoad(EventArgs e)
    {
        Pal().TraceError();

        async Task Pal()
        {
            var repo = await RecordRepo.Create(_dir, _ctsIndexing.Token);
            repoView.Data = new RecordRepoViewModel(repo);
            panelIndexing.Hide();
        }
    }
}
