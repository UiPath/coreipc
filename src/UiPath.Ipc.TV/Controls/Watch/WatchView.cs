using BrightIdeasSoftware;
using System.ComponentModel;

namespace UiPath.Ipc.TV;

public partial class WatchView : UserControl
{
    private readonly TreeListView _treeListView;

    private ValueSource? _model;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ValueSource? Model
    {
        get => _model;
        set
        {
            if (_model == value)
            {
                return;
            }

            if (_model is not null)
            {
                DetachModel();
            }
            _model = value;
            if (_model is not null)
            {
                AttachModel();
            }
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public event Action<string>? SelectRecord;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ExpandRootChildrenOnAttach { get; set; } = true;

    public WatchView()
    {
        InitializeComponent();
        _treeListView = CreateTreeListView();
        _treeListView.Stylize();
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
            Columns =
            {
                new OLVColumn()
                {
                    Text = "Name",
                    AspectName = "Name",
                    Width = 250,
                    ImageGetter = node => (node as WatchNode)?.ImageKey,                    
                },
                new OLVColumn()
                {
                    Text = "Value",
                    AspectName = "Value",
                    Width = 350,
                },
                new OLVColumn()
                {
                    Text = "Action",
                    IsButton = true,
                    AspectGetter = node => (node as WatchNode)?.GetActionName(),
                    Width = 100,
                    ButtonSizing = OLVColumn.ButtonSizingMode.CellBounds,
                    ButtonPadding = new Size(2, 2),
                },
                new OLVColumn()
                {
                    Text = "Type",
                    AspectName = "Type",
                    Width = 300,
                }
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

        treeListView.ButtonClick += (sender, e) =>
        {
            var node = (e.Model as WatchNode)!;

            if (node.IsReference)
            {
                if (node.GetReference() is { } id)
                {
                    SelectRecord?.Invoke(id);
                }

                return;
            }

            if (node.ObjectValue is string str)
            {
                StringViewer.ShowString(node.Name, str);
                return;
            }

            MessageBox.Show("Not implemented yet.");
        };

        return treeListView;
    }

    private void AttachModel()
    {
        var root = new WatchNode(_model!);
        _treeListView.SetObjects(new object[] { root });
        _treeListView.Expand(root);
        if (ExpandRootChildrenOnAttach)
        {
            foreach (var child in root.Children)
            {
                if (child.ObjectValue is not DateTime)
                {
                    _treeListView.Expand(child);
                }
            }
        }
    }
    private void DetachModel()
    {
        _treeListView.SetObjects(Array.Empty<object>());
    }
}

