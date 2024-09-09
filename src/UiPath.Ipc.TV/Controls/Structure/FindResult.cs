using System.ComponentModel;

namespace UiPath.Ipc.TV;

public partial class FindResultView : UserControl
{
    private FindResultModel? _model;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal FindResultModel? Model
    {
        get => _model;
        set
        {
            _model = value;
            label1.Text = value?.Title ?? "No data";
            telemetryExplorer1.Model = value?.Model;
        }
    }

    public event Action? Closed;

    public FindResultView()
    {
        InitializeComponent();
        telemetryExplorer1.NoDetailsPane = true;
    }
}

internal readonly record struct FindResultModel(string Title, RelationalTelemetryModel Model);
