namespace CustomerBinding;

public partial class MainForm : Form
{
    private readonly CustomerViewModel _viewModel = new();

    public MainForm()
    {
        InitializeComponent();
    }
}
