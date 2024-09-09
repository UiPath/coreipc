using BrightIdeasSoftware;
using Microsoft.EntityFrameworkCore;
using System.Collections;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using UiPath.Ipc.TV.DataAccess;

namespace UiPath.Ipc.TV;

// Expression<Func<RecordEntity, bool>>;

public partial class RepoView : UserControl
{
    private RecordRepoViewModel? _data;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RecordRepoViewModel? Data
    {
        get => _data;
        set
        {
            if (_data == value)
            {
                return;
            }

            if (_data is not null)
            {
                DetachRepo();
            }
            _data = value;
            if (_data is not null)
            {
                AttachRepo();
            }
        }
    }

    private RecordEntity? SelectedEntity => _data is not null && listView.SelectedIndices.Count is 1
        ? _data.Get(listView.SelectedIndices[0])
        : null;

    private readonly Func<Task<QueryableFactory>> _dbQueryPredicate;
    private Lazy<Task<QueryableFactory>> _cachedCompilation = null!;
    private void ResetCompilationCache() => _cachedCompilation = new(_dbQueryPredicate);
    private async Task<QueryableFactory?> GetCompilation()
    {
        try
        {
            return await _cachedCompilation.Value;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }    

    public RepoView()
    {
        InitializeComponent();
        _dbQueryPredicate = ConfigureDbQueryPredicate();
        ResetCompilationCache();
    }

    private Func<Task<QueryableFactory>> ConfigureDbQueryPredicate()
    {
        if (DesignMode)
        {
            return null!;
        }

        eeDbQueryPredicate.ShowLineNumbers = false;

        eeDbQueryPredicate.EnsureAccessible([Assembly.Load("System.Runtime")]);
        eeDbQueryPredicate.EnsureAccessible(
        [
            typeof(object),
            typeof(Microsoft.CSharp.RuntimeBinder.Binder),
            typeof(Queryable),
            typeof(Enumerable),
            typeof(IQueryable<>),
            typeof(IEnumerable<>),
            typeof(DbSet<>),
            typeof(Telemetry),
            typeof(RepoView),
            typeof(Expression<>),
            typeof(RecordEntity),
        ]);

        eeDbQueryPredicate.Code = $$"""
            context => 
            {
                var query = from record in context.Records
                            where 1 == 1
                            orderby record.CreatedAtUtc ascending
                            select record;

                return query;
            }
            """;

        eeDbQueryPredicate.CodeChanged += EeDbQueryPredicate_CodeChanged;

        return eeDbQueryPredicate.ConfigureExecution<QueryableFactory>();
    }

    private void EeDbQueryPredicate_CodeChanged(object? sender, EventArgs e)
    {
        ResetCompilationCache();
    }

    private TreeListView CreateTreeListView()
    {
        var treeListView = new TreeListView()
        {
            Parent = this,
            Dock = DockStyle.Fill,
            SmallImageList = imageList,
            ChildrenGetter = node => (node as WatchNode)?.Children,
            CanExpandGetter = node => (node as WatchNode)?.Children.Count > 0,
            VirtualListDataSource = null,
            VirtualMode = true,
            View = View.Details,
            Columns =
            {
                new OLVColumn()
                {
                    Text = "CreatedAtUtc",
                    AspectName = "CreatedAtUtc",
                    Width = 250,
                    ImageGetter = node => (node as WatchNode)?.ImageKey,
                    Tag = RecordOrdererPair.Create(x => x.CreatedAtUtc)
                },
                new OLVColumn()
                {
                    Text = "Source",
                    AspectName = "FileName",
                    Width = 350,
                    Tag = RecordOrdererPair.Create(x => x.FileName)
                },
                new OLVColumn()
                {
                    Text = "Kind",
                    AspectName = "RecordKind",
                    Width = 350,
                    Tag = RecordOrdererPair.Create(x => x.RecordKind)
                },
            }
        };

        var italic = new Font(treeListView.Font, FontStyle.Italic);

        treeListView.FormatRow += (sender, e) =>
        {
            if (e.Model is not WatchNode node) { return; }

            if (node.IsReference)
            {
                e.Item.ForeColor = Color.Fuchsia;
                e.Item.Font = italic;
            }
        };

        return treeListView;
    }

    private void AttachRepo()
    {
        _data!.DataChanged += RepoView_DataChanged;
        RefreshView();
    }

    private void RepoView_DataChanged(object? sender, EventArgs e)
    {
        RefreshView();
    }

    private void RefreshView()
    {
        listView.VirtualListSize = _data?.GetCount() ?? 0;
    }

    private void DetachRepo()
    {
        _data!.DataChanged -= RepoView_DataChanged;
        _data = null;
        RefreshView();
    }

    private void listView_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
    {
        if (_data is not null)
        {
            e.Item = CreateView(_data.Get(e.ItemIndex));
        }
    }

    private ListViewItem CreateView(RecordEntity recordEntity)
    {
        return new ListViewItem(recordEntity.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss.fff"))
        {
            SubItems =
            {
                recordEntity.FileName,
                recordEntity.RecordKind.ToString(),
            },
            IndentCount = 0,
            ImageIndex = -1
        };
    }

    private void listView_CacheVirtualItems(object sender, CacheVirtualItemsEventArgs e)
    {
        _data?.Cache(e.StartIndex, e.EndIndex);
    }

    private void toolStripButton1_Click(object sender, EventArgs e)
    {
        ApplyFilter();
    }

    private bool _applyingFilter = false;

    private void ApplyFilter()
    {
        Pal().TraceError();

        async Task Pal()
        {
            if (_applyingFilter)
            {
                return;
            }
            buttonRun.Enabled = false;
            progressBar.Visible = true;
            _applyingFilter = true;
            try
            {
                var maybeQueryableFactory = await GetCompilation();
                
                if (Data is not null)
                {
                    Data.QueryableFactory = maybeQueryableFactory ?? RecordRepoViewModel.DefaultQueryableFactory;
                }
            }
            finally
            {
                _applyingFilter = false;
                buttonRun.Enabled = true;
                progressBar.Visible = false;
            }
        }
    }

    private void listView_SelectedIndexChanged(object sender, EventArgs e)
    {
        detailsPane1.Model = SelectedEntity;
        if (SelectedEntity is not null)
        {
            splitContainer2.Panel2Collapsed = false;
        }
    }
}
