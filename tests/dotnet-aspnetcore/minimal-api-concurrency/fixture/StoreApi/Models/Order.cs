namespace StoreApi.Models;

public class Order
{
    public int Id { get; set; }

    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public OrderStatus Status { get; set; }

    // Many-to-many to Product through the OrderItem join entity.
    public List<OrderItem> Items { get; set; } = new();
}

public enum OrderStatus
{
    Pending,
    Paid,
    Shipped,
    Delivered,
    Cancelled
}
