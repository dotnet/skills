namespace Store;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("productdetails", typeof(ProductDetailsPage));
    }
}
