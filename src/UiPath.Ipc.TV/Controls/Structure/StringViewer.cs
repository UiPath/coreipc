namespace UiPath.Ipc.TV;

public partial class StringViewer : Form
{
    public static void ShowString(string title, string value)
    {
        var form = new StringViewer { Text = title };
        form.textBox.Text = value;
        form.ShowDialog();
    }

    public StringViewer()
    {
        InitializeComponent();
    }
}
