namespace OrderEntry;

public partial class MainForm : Form
{
    public MainForm()
    {
        InitializeComponent();
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        _statusLabel.Text = $"Committed {_orderNumberTextBox.Text}";
    }
}
