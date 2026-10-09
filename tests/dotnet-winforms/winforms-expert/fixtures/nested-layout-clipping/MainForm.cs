namespace ShippingEditor;

public partial class MainForm : Form
{
    public MainForm()
    {
        InitializeComponent();
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        Text = $"{_streetTextBox.Text}, {_cityTextBox.Text} {_postalCodeTextBox.Text}";
    }
}
