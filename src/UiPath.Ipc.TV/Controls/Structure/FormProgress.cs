namespace UiPath.Ipc.TV;

public partial class FormProgress : Form, IProgress<(string? label, int cTotal, int cProcessed)>
{
    public static T ExecuteOnThreadPool<T>(
        Func<FormProgress, Task<T>> func)
        //Func<IProgress<TProgressReport>, CancellationToken, Task<T>> func,
        //Func<TProgressReport, (string? label, int cTotal, int cProcessed)> progressTranslator)
    {
        var form = new FormProgress();

        //var progress = form
        //    .Select(progressTranslator)
        //    .ScheduleOn(TaskScheduler.FromCurrentSynchronizationContext());

        var task = func(form);
        // var task = func(progress, form._cts.Token);
        (task as Task).ContinueWith(task =>
        {
            form.DialogResult = DialogResult.OK;
        });

        form.ShowDialog();

        if (task.Exception is not null)
        {
            throw task.Exception;
        }

        return task.Result;
    }

    internal readonly CancellationTokenSource _cts = new();


    public void Report((string? label, int cTotal, int cProcessed) value)
    {
        label.Text = value.label ?? $"{value.cProcessed} / {value.cTotal} done...";
        progressBar.Maximum = value.cTotal;
        progressBar.Value = value.cProcessed;
    }

    public FormProgress()
    {
        InitializeComponent();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cts.Dispose();
    }

    private void InitializeComponent()
    {
        panel1 = new Panel();
        buttonCancel = new Button();
        progressBar = new ProgressBar();
        label = new Label();
        panel1.SuspendLayout();
        SuspendLayout();
        // 
        // panel1
        // 
        panel1.BorderStyle = BorderStyle.Fixed3D;
        panel1.Controls.Add(buttonCancel);
        panel1.Controls.Add(progressBar);
        panel1.Controls.Add(label);
        panel1.Dock = DockStyle.Fill;
        panel1.Location = new Point(0, 0);
        panel1.Name = "panel1";
        panel1.Size = new Size(391, 195);
        panel1.TabIndex = 0;
        // 
        // buttonCancel
        // 
        buttonCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        buttonCancel.Location = new Point(295, 154);
        buttonCancel.Name = "buttonCancel";
        buttonCancel.Size = new Size(82, 27);
        buttonCancel.TabIndex = 2;
        buttonCancel.Text = "Cancel";
        buttonCancel.UseVisualStyleBackColor = true;
        buttonCancel.Click += buttonCancel_Click;
        // 
        // progressBar
        // 
        progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        progressBar.Location = new Point(10, 38);
        progressBar.Name = "progressBar";
        progressBar.Size = new Size(367, 23);
        progressBar.TabIndex = 1;
        // 
        // label
        // 
        label.AutoSize = true;
        label.Location = new Point(10, 16);
        label.Name = "label";
        label.Size = new Size(83, 19);
        label.TabIndex = 0;
        label.Text = "Processing...";
        // 
        // FormProgress
        // 
        CancelButton = buttonCancel;
        ClientSize = new Size(391, 195);
        Controls.Add(panel1);
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point, 0);
        FormBorderStyle = FormBorderStyle.None;
        Name = "FormProgress";
        panel1.ResumeLayout(false);
        panel1.PerformLayout();
        ResumeLayout(false);
    }

    private Panel panel1;
    private ProgressBar progressBar;
    private Label label;
    private Button buttonCancel;

    private void buttonCancel_Click(object sender, EventArgs e)
    {
        if (MessageBox.Show("Are you sure you want to cancel this process?", "Question", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            is DialogResult.No)
        {
            return;
        }

        DialogResult = DialogResult.Cancel;
    }
}
