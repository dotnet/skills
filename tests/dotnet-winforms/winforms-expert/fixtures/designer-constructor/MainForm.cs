namespace DesignerConstructor;

public partial class MainForm : Form
{
    private readonly ICustomerService _customerService;

    public MainForm(ICustomerService customerService)
    {
        _customerService = customerService;
        InitializeComponent();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _customerNameLabel.Text = _customerService.GetCurrentCustomerName();
    }
}
