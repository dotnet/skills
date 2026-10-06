using System.Collections.ObjectModel;

namespace LiveListBinding;

public partial class MainForm : Form
{
    private readonly ObservableCollection<Customer> _customers =
    [
        new Customer("Ada")
    ];

    public MainForm()
    {
        InitializeComponent();
        _customerListBox.DataSource = _customers;
        _customerListBox.DisplayMember = nameof(Customer.Name);
    }

    private void AddButton_Click(object? sender, EventArgs e)
    {
        _customers.Add(new Customer($"Customer {_customers.Count + 1}"));
    }
}
