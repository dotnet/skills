namespace OrderService.Tests;

public sealed class FakeDatabase
{
}

public sealed class FakeInventory
{
}

public sealed class FakeLogger
{
}

public sealed class FakeEmailSender
{
    private readonly HashSet<string> _notifiedOrderIds = [];

    public void RecordNotification(string orderId) => _notifiedOrderIds.Add(orderId);

    public bool WasNotificationSent(string orderId) => _notifiedOrderIds.Contains(orderId);
}

public sealed class Order
{
    public string Id { get; set; } = "ORD-001";

    public List<OrderItem> Items { get; } = [];

    public decimal TotalAmount { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal GrandTotal { get; set; }

    public string Status { get; set; } = "Pending";
}

public sealed record OrderItem(string Sku, int Quantity);

public sealed record CreditCard(string Number);

public sealed record OrderResult(decimal TotalAmount, string Status);

public sealed record OrderSummary(string Id, int ItemCount, decimal TotalAmount)
{
    public override string ToString() =>
        FormattableString.Invariant(
            $"Order {Id}: {ItemCount} item(s), Total: ${TotalAmount:0.00}");
}

public sealed class ValidationException(string message) : Exception(message)
{
}

public sealed class OrderProcessor(
    FakeDatabase database,
    FakeEmailSender email,
    FakeInventory inventory)
{
    public OrderResult ProcessOrder(Order order)
    {
        ValidateOrder(order);
        return new OrderResult(order.TotalAmount, "StandardProcessed");
    }

    public void ProcessOrderAsync(Order order)
    {
        ProcessOrder(order);
        email.RecordNotification(order.Id);
    }

    public void ValidateOrder(Order? order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.Items.Count == 0)
        {
            throw new ValidationException("Order must contain at least one item");
        }
    }

    public void CalculateTotal(Order order)
    {
        order.TotalAmount = 247.50m;
        order.TaxAmount = 22.28m;
        order.GrandTotal = 269.78m;
    }

    public void ApplyDiscount(Order order, string code) => _ = (order, code);

    public void ReserveInventory(Order order) => _ = (order, inventory);

    public void ProcessPayment(Order order, CreditCard card) => _ = (order, card, database);

    public void SendConfirmation(Order order) => _ = order;

    public void UpdateOrderHistory(Order order) => order.Status = "Completed";

    public OrderSummary GetOrderSummary(Order order) =>
        new(order.Id, order.Items.Count, order.TotalAmount);

    public List<Order> ImportOrders(string csv) => [new(), new(), new(), new(), new()];
}
