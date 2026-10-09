public static class Checks
{
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    public static void Main()
    {
        var products = Menu.Products();
        Require(products.Route == "products" && products.Items.Count == 1, "unexpected flyout hierarchy");
        var tab = products.Items.Single();
        Require(tab.Route == "status", "parent route changed");
        Require(tab.Items.Select(p => p.Route).SequenceEqual(new[] { "active", "archived" }),
            "subtabs require distinct stable routes");
        Require(tab.Items.All(p => p.ContentTemplate is not null && p.Content is null),
            "pages should be lazy");
        Require(tab.Items.Select(p => p.Title).SequenceEqual(new[] { "Active", "Archived" }),
            "visible tab labels changed");
        Console.WriteLine("PASS: behavior-contract");
    }
}
