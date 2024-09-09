using System.ComponentModel;

namespace UiPath.Ipc.TV;

public partial class FormFilter : Form, IProgress<FilterProgressReport>
{
    private const string StatusApplyingFilter = "Applying filter";

    private readonly IDisposable _subscription;
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
    private RelationalTelemetryModel? FilteredModel
    {
        get => telemetryExplorer1.Model;
        set => telemetryExplorer1.Model = value;
    }

    public FormFilter(FormMain mdiParent, FormProjectModel projectModel)
    {
        InitializeComponent();
        ConfigureExpressionEditor();

        MdiParent = mdiParent;
        _subscription = projectModel.RelationalModels.Subscribe(relationalModel => Model = relationalModel);
    }

    protected override void OnClosed(EventArgs e)
    {
        _subscription.Dispose();
        base.OnClosed(e);
    }

    private void ConfigureExpressionEditor()
    {
        if (DesignMode)
        {
            return;
        }

        expressionEditor.References = [
            typeof(object).Assembly,
            typeof(Telemetry).Assembly,
            typeof(FormFilter).Assembly
        ];
        expressionEditor.Usings = [
            typeof(object).Namespace!,
            typeof(Telemetry).Namespace!,
            typeof(FormFilter).Namespace!,
        ];
        expressionEditor.Code = $$"""
            record => 
            {
                return record is Telemetry.MaybeRunBeforeCall maybeRunBeforeCall && maybeRunBeforeCall.BeforeCallIsNotNull;
            }
            """;
        expressionEditor.ReturnType = "Func<Telemetry.RecordBase, bool>";
    }

    private void DetachModel()
    {
        Enabled = false;
    }

    private void AttachModel()
    {
        Enabled = true;
    }

    private void buttonExecute_Click(object sender, EventArgs e)
    {
        ExecuteFilter().TraceError();
    }

    private async Task ExecuteFilter()
    {
        buttonExecute.Enabled = false;
        buttonExecute.Text = "Executing...";
        buttonCancel.Visible = true;
        using var cts = new CancellationTokenSource();
        buttonCancel.Click += CancelClicked;
        labelStatus.Text = StatusApplyingFilter;
        progressBar.Visible = true;

        try
        {
            var predicate = await expressionEditor.Execute<Func<Telemetry.RecordBase, bool>>();

            var filter = new ModelFilter { Predicate = predicate };

            FilteredModel = await ModelFilterExecutor.ExecuteAsync(
                _model!, 
                filter, 
                progress: this.ScheduleOn(TaskScheduler.FromCurrentSynchronizationContext()));
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == cts.Token)
        {
            // ignore
        }
        finally
        {
            progressBar.Visible = false;
            labelStatus.Text = $"Filtered: {FilteredModel!.Records.Count} out of {Model!.Records.Count}";
            buttonCancel.Click -= CancelClicked;
            buttonCancel.Visible = false;
            buttonExecute.Text = "Execute";
            buttonExecute.Enabled = true;
        }

        void CancelClicked(object? sender, EventArgs e)
        {
            cts.Cancel();
        }
    }

    void IProgress<FilterProgressReport>.Report(FilterProgressReport value)
    {
        labelStatus.Text = $"{StatusApplyingFilter}: {value.CProcessed} / {value.CTotal} ({value.CPassed} passed)";
        progressBar.Maximum = value.CTotal;
        progressBar.Value = value.CProcessed;
    }
}
