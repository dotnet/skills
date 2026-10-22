namespace CustomerEditor;

public partial class MainForm : Form
{
    public MainForm()
    {
        InitializeComponent();
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        Text = $"Saved: {_customerNameTextBox.Text}";
    }
}
