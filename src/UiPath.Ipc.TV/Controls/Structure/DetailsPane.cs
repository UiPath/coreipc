using System.ComponentModel;

namespace UiPath.Ipc.TV;

public partial class DetailsPane : UserControl
{
    private RelationalRecord? _model;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal RelationalRecord? Model
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
    public event Action<string>? SelectRecord
    {
        add => watchRecord.SelectRecord += value;
        remove => watchRecord.SelectRecord -= value;
    }

    public DetailsPane()
    {
        InitializeComponent();
        UpdateExceptionVisibility();
    }

    private void UpdateExceptionVisibility()
    {
        Telemetry.ExceptionInfo? exceptionInfo = null;
        splitContainer.Panel2Collapsed = _model?.IsError(out exceptionInfo) is not true;
        if (exceptionInfo is not null)
        {
            watchException.Model = new ValueSource.VirtualVariable("$exception", exceptionInfo.TypeName[0..exceptionInfo.TypeName.IndexOf(',')], exceptionInfo);
        }
        else
        {
            watchException.Model = null;
        }
    }

    private void DetachModel()
    {
        watchRecord.Model = null;
        UpdateExceptionVisibility();
    }
    private void AttachModel()
    {
        watchRecord.Model = new ValueSource.Variable("$record", typeof(Telemetry.RecordBase), _model!.Record);
        UpdateExceptionVisibility();
    }
}
