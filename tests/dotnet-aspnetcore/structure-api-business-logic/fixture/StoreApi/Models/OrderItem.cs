namespace StoreApi.Models;

// Join entity carrying payload for the Order <-> Product many-to-many relationship.
public class OrderItem
{
    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
