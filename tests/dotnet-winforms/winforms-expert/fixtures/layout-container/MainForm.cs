namespace ContactEditor;

public partial class MainForm : Form
{
    public MainForm()
    {
        InitializeComponent();
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        Text = $"{_firstNameTextBox.Text} {_lastNameTextBox.Text}";
    }
}
