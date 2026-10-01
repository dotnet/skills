using System.Collections.ObjectModel;
using Store.Models;

namespace Store.ViewModels;

public sealed class CatalogViewModel
{
    public ObservableCollection<Product> Products { get; } = [];

    public Product? SelectedProduct { get; set; }
}
