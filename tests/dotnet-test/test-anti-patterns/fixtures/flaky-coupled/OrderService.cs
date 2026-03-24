namespace FlakyCoupled;

public class Order
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ItemName { get; set; } = "";
    public int Quantity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}

public class OrderService
{
    private readonly Dictionary<string, Order> _orders = new();

    public Order CreateOrder(string itemName, int quantity)
    {
        var order = new Order { ItemName = itemName, Quantity = quantity };
        _orders[order.Id] = order;
        return order;
    }

    public void ProcessOrder(string orderId)
    {
        if (!_orders.ContainsKey(orderId))
            throw new InvalidOperationException($"Order {orderId} not found");
        _orders[orderId].ProcessedAt = DateTime.UtcNow;
    }

    public Order GetOrder(string orderId) => _orders[orderId];
    public List<Order> GetAllOrders() => _orders.Values.ToList();
}
