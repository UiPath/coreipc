using System.ComponentModel;

namespace UiPath.Ipc.TV;

public partial class TelemetryExplorer : UserControl
{
    private RelationalTelemetryModel? _model;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal RelationalTelemetryModel? Model
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
    public bool NoDetailsPane { get; set; }

    private RelationalRecord? SelectedRecord => listView.SelectedIndices.Count is 1
        ? _model!.TimestampOrder[listView.SelectedIndices[0]]
        : null;

    private readonly Dictionary<string, int> _imageKeyToIndex;

    public TelemetryExplorer()
    {
        InitializeComponent();

        detailsPane.SelectRecord += SelectRecord;   

        UpdateDetailsPane();

        _imageKeyToIndex = imageList.Images.Keys.Cast<string>().ToDictionary(key => key, imageList.Images.IndexOfKey);
    }

    private void SelectRecord(string id)
    {        
        if (_model?.IdToIndex.TryGetValue(new RecordId(id), out var index) is not true)
        {
            return;
        }

        listView.SelectedIndices.Clear();
        listView.SelectedIndices.Add(index);
        listView.EnsureVisible(index);
    }

    private void DetachModel()
    {
        panelNoModel.Visible = true;
        panelNoModel.BringToFront();
    }
    private void AttachModel()
    {
        panelNoModel.Visible = false;
        listView.VirtualListSize = _model!.Records.Count;
        listView.AutoSizeColumns();
    }

    private void listView_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
    {
        var record = _model!.TimestampOrder[e.ItemIndex];
        e.Item = CreateView(record);
    }

    private ListViewItem CreateView(RelationalRecord record)
    {
        return new ListViewItem(record.Record.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss.fff"))
        {
            SubItems =
            {
                record.Origin.File.Name,
                record.Record.GetType().Name
            },
            IndentCount = record.GetVisualIndentation(),
            ImageIndex = DetermineImageIndex()
        };

        int DetermineImageIndex()
        {
            if (!_imageKeyToIndex.TryGetValue(DetermineImageKey(), out var index))
            {
                return -1;
            }
            return index;
        }
        string DetermineImageKey()
        {
            if (record.IsError(out _))
            {
                return "Error";
            }

            return "";
        }
    }

    private void listView_SelectedIndexChanged(object sender, EventArgs e)
    {
        UpdateDetailsPane();
    }

    private void UpdateDetailsPane()
    {
        var selectedRecord = SelectedRecord;
        if (selectedRecord is not null)
        {
            splitContainer.Panel2Collapsed = NoDetailsPane;
        }        
        detailsPane.Model = selectedRecord;
    }
}
