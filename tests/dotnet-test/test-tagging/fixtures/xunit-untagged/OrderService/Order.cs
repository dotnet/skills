namespace OrderService;

public sealed class Order
{
    public int Id { get; set; }
    public string CustomerEmail { get; set; } = string.Empty;
    public List<OrderItem> Items { get; set; } = [];
    public decimal Total => Items.Sum(i => i.Price * i.Quantity);
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
}

public sealed class OrderItem
{
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
}

public enum OrderStatus
{
    Pending,
    Confirmed,
    Shipped,
    Cancelled
}
