using System.ComponentModel;
using UiPath.Ipc.TV.DataAccess;

namespace UiPath.Ipc.TV;

public partial class DetailsPane2 : UserControl
{
    private RecordEntity? _model;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RecordEntity? Model
    {
        get => _model;
        set
        {
            _model = value; 
            Manifest();
        }
    }

    private void Manifest()
    {
        if (_model is not null)
        {
            watchRecord.Model = new ValueSource.Variable("entity", typeof(RecordEntity), _model);
        }
        else
        {
            watchRecord.Model = null;
        }
    }

    public DetailsPane2()
    {
        InitializeComponent();
    }
}
