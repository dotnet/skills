using System.Collections.ObjectModel;
using Warehouse.Models;

namespace Warehouse.ViewModels;

public sealed class InventoryViewModel
{
    public ObservableCollection<InventoryItem> Items { get; } = [];
}
