namespace Contoso.Orders;

public sealed class OrderService
{
    private readonly List<Order> _orders = [];

    public Order CreateOrder(string customerId, List<OrderItem> items)
    {
        ArgumentNullException.ThrowIfNull(customerId);
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
            throw new InvalidOperationException("Cannot create an order with no items.");

        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            CustomerId = customerId,
            Items = items,
            Total = items.Sum(i => i.Price * i.Quantity),
            CreatedAt = DateTime.UtcNow
        };

        _orders.Add(order);
        return order;
    }

    public Order? GetOrder(string orderId) =>
        _orders.FirstOrDefault(o => o.Id == orderId);

    public IReadOnlyList<Order> GetOrdersByCustomer(string customerId) =>
        _orders.Where(o => o.CustomerId == customerId).ToList();
}

public sealed class Order
{
    public required string Id { get; init; }
    public required string CustomerId { get; init; }
    public required List<OrderItem> Items { get; init; }
    public required decimal Total { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed class OrderItem
{
    public required string ProductName { get; init; }
    public required decimal Price { get; init; }
    public required int Quantity { get; init; }
}
