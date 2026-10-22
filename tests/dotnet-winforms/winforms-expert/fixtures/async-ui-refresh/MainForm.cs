namespace AsyncCustomerEditor;

public partial class MainForm : Form
{
    public MainForm()
    {
        InitializeComponent();
    }

    private void RefreshButton_Click(object? sender, EventArgs e)
    {
        _refreshButton.Enabled = false;
        _statusLabel.Text = "Refreshing...";

        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            var status = DateTimeOffset.Now.ToString("O");
            BeginInvoke(() =>
            {
                _statusLabel.Text = $"Updated {status}";
                _refreshButton.Enabled = true;
            });
        });
    }
}
