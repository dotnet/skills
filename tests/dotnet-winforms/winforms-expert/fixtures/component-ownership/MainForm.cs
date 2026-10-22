namespace ComponentOwnership;

public partial class MainForm : Form
{
    public MainForm()
    {
        InitializeComponent();
    }

    private void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        _statusLabel.Text = DateTime.Now.ToLongTimeString();
    }
}
