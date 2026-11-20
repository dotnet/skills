using System.Collections.ObjectModel;
using Microsoft.Maui.Controls;
public sealed record Product(string Name, decimal Price);
public sealed class ProductsViewModel
{
    public ObservableCollection<Product> Products { get; } = new();
}
public static class ProductList
{
    public static CollectionView Create(ProductsViewModel vm) => new CollectionView
    {
        BindingContext = vm,
        ItemsSource = vm.Products,
        ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem,
        ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical)
    };
}
