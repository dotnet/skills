namespace SafeDesigner;

public partial class MainForm : Form
{
    public MainForm()
    {
        InitializeComponent();
    }

    private void RefreshButton_Click(object? sender, EventArgs e)
    {
        _statusLabel.Text = DateTime.Now.ToLongTimeString();
    }
}
