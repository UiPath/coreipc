namespace UiPath.Ipc.TV;

public partial class FindResultSetView : UserControl
{
    private readonly List<FindResultView> _results = new();

    public FindResultSetView()
    {
        InitializeComponent();
    }

    internal void Add(FindResultModel model)
    {
        var page = new TabPage();
        var view = new FindResultView()
        {
            Parent = page,
            Dock = DockStyle.Fill,
            Model = model
        };
        
        _results.Add(view);
        tabControl1.TabPages.Add(page);

        view.Closed += () =>
        {
            _results.Remove(view);
            tabControl1.TabPages.Remove(page);
        };
    }
}

