namespace MyApp;

public class OrderService
{
    private readonly IOrderRepository _repository;

    public OrderService(IOrderRepository repository)
    {
        _repository = repository;
    }

    // Complexity: 7 (if, if, ||, foreach, if, if)
    public OrderResult ProcessOrder(Order order)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));

        if (order.Items == null || order.Items.Count == 0)
            return new OrderResult { Success = false, Error = "No items" };

        decimal total = 0;
        foreach (var item in order.Items)
        {
            if (item.Quantity <= 0)
                return new OrderResult { Success = false, Error = "Invalid quantity" };
            total += item.Price * item.Quantity;
        }

        if (total <= 0)
            return new OrderResult { Success = false, Error = "Invalid total" };

        order.Total = total;
        order.Status = OrderStatus.Confirmed;
        _repository.Save(order);

        return new OrderResult { Success = true, OrderId = order.Id, Total = total };
    }

    // Complexity: 2
    public void CancelOrder(int orderId)
    {
        var order = _repository.GetById(orderId);
        if (order != null)
        {
            order.Status = OrderStatus.Cancelled;
            _repository.Save(order);
        }
    }
}

public class Order
{
    public int Id { get; set; }
    public List<OrderItem> Items { get; set; } = new();
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; }
}

public class OrderItem
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int Quantity { get; set; }
}

public class OrderResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int OrderId { get; set; }
    public decimal Total { get; set; }
}

public enum OrderStatus { Pending, Confirmed, Shipped, Cancelled }

public interface IOrderRepository
{
    Order? GetById(int id);
    void Save(Order order);
}
