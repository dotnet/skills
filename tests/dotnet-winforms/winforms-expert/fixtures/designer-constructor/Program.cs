namespace DesignerConstructor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(new CustomerService()));
    }
}

internal sealed class CustomerService : ICustomerService
{
    public string GetCurrentCustomerName() => "Ada";
}
