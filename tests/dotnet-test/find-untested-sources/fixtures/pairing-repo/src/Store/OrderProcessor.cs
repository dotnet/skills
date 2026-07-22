using System.Collections.Generic;
using System.Linq;

namespace Store;

public sealed class OrderProcessor
{
    public decimal CalculateTotal(IEnumerable<decimal> prices)
        => prices.Sum();

    public bool CanShip(string country, decimal total)
        => country == "US" || total >= 100m;

    public string CreateReference(int orderId)
        => $"ORD-{orderId:D6}";
}
