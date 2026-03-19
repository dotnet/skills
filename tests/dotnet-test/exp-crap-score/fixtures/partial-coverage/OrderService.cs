namespace MyApp;

public class OrderService
{
    private readonly IOrderRepository _repository;
    private readonly IPaymentGateway _paymentGateway;

    public OrderService(IOrderRepository repository, IPaymentGateway paymentGateway)
    {
        _repository = repository;
        _paymentGateway = paymentGateway;
    }

    // Complexity: 10 (if, if, ||, for, if, ||, ??, if, if)
    public OrderResult ProcessOrder(Order order)
    {
        if (order == null)
            throw new ArgumentNullException(nameof(order));

        if (order.Items == null || order.Items.Count == 0)
            return new OrderResult { Success = false, Error = "No items in order" };

        decimal total = 0;
        for (int i = 0; i < order.Items.Count; i++)
        {
            var item = order.Items[i];
            if (item.Quantity <= 0 || item.Price < 0)
                return new OrderResult { Success = false, Error = $"Invalid item at index {i}" };

            var discount = item.Discount ?? 0m;
            total += (item.Price - discount) * item.Quantity;
        }

        if (total <= 0)
            return new OrderResult { Success = false, Error = "Order total must be positive" };

        var payment = _paymentGateway.Charge(order.CustomerId, total);
        if (!payment.Succeeded)
            return new OrderResult { Success = false, Error = payment.ErrorMessage };

        order.Total = total;
        order.Status = OrderStatus.Confirmed;
        _repository.Save(order);

        return new OrderResult { Success = true, OrderId = order.Id, Total = total };
    }

    // Complexity: 6 (if, ||, foreach, if, &&)
    public decimal CalculateTotal(List<OrderItem> items)
    {
        if (items == null || items.Count == 0)
            return 0;

        decimal total = 0;
        foreach (var item in items)
        {
            if (item.Quantity > 0 && item.Price >= 0)
                total += item.Price * item.Quantity;
        }
        return total;
    }

    // Complexity: 7 (if, 4 switch arms [_ default excluded], ?:)
    public string GetOrderStatus(int orderId)
    {
        var order = _repository.GetById(orderId);
        if (order == null)
            return "Not Found";

        return order.Status switch
        {
            OrderStatus.Pending => "Pending",
            OrderStatus.Confirmed => "Confirmed",
            OrderStatus.Shipped => order.TrackingNumber != null ? "Shipped (Tracked)" : "Shipped",
            OrderStatus.Cancelled => "Cancelled",
            _ => "Unknown"
        };
    }

    // Complexity: 3 (if, &&)
    public void CancelOrder(int orderId)
    {
        var order = _repository.GetById(orderId);
        if (order != null && order.Status != OrderStatus.Shipped)
        {
            order.Status = OrderStatus.Cancelled;
            _repository.Save(order);
        }
    }
}

public class Order
{
    public int Id { get; set; }
    public string CustomerId { get; set; } = "";
    public List<OrderItem> Items { get; set; } = new();
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; }
    public string? TrackingNumber { get; set; }
}

public class OrderItem
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public decimal? Discount { get; set; }
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

public interface IPaymentGateway
{
    PaymentResult Charge(string customerId, decimal amount);
}

public class PaymentResult
{
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
}
