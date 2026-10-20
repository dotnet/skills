using Microsoft.Maui.Controls;
public static class Menu
{
    public static FlyoutItem Products()
    {
        var products = new FlyoutItem { Title = "Products", Route = "products" };
        var tab = new Tab { Route = "status" };
        tab.Items.Add(new ShellContent { Title = "Active", Route = "active",
            ContentTemplate = new DataTemplate(() => new ContentPage()) });
        tab.Items.Add(new ShellContent { Title = "Archived", Route = "active",
            ContentTemplate = new DataTemplate(() => new ContentPage()) });
        products.Items.Add(tab);
        return products;
    }
}
