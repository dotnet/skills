public static class Checks
{
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    public static void Main()
    {
        var vm = new ProductsViewModel();
        var list = ProductList.Create(vm);
        Require(ReferenceEquals(list.BindingContext, vm) && ReferenceEquals(list.ItemsSource, vm.Products),
            "list must retain its real data source");
        Require(list.ItemSizingStrategy == Microsoft.Maui.Controls.ItemSizingStrategy.MeasureFirstItem,
            "uniform rows should reuse the first measurement");
        vm.Products.Add(new Product("Pen", 2m));
        Require(vm.Products.Count == 1, "observable source changed");
        Console.WriteLine("PASS: behavior-contract");
    }
}
