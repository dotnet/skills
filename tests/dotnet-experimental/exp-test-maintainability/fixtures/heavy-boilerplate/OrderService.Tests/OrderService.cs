namespace OrderService.Tests;

public sealed class Order
{
    public required string CustomerId { get; init; }
    public required string CustomerEmail { get; init; }
    public bool IsPremium { get; init; }
    public required List<OrderItem> Items { get; init; }
}

public sealed class OrderItem
{
    public required string ProductId { get; init; }
    public required string Name { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}

public sealed record ProcessResult(bool Success, string Status, decimal Total);
public sealed record OrderValidationResult(bool IsValid, string? ErrorMessage);
public sealed record SentEmail(string To);

public sealed class FakeLogger
{
    public List<string> Entries { get; } = [];

    public void Log(string entry) => Entries.Add(entry);
}

public sealed class FakeEmailService
{
    public List<SentEmail> SentEmails { get; } = [];

    public void Send(string to) => SentEmails.Add(new SentEmail(to));
}

public sealed class FakeInventoryService(int stockLevel)
{
    public bool HasStock(Order order) =>
        order.Items.All(item => item.Quantity <= stockLevel);
}

public sealed class OrderProcessor(
    FakeLogger logger,
    FakeEmailService emailService,
    FakeInventoryService inventory)
{
    public ProcessResult Process(Order order)
    {
        logger.Log($"Processing order for {order.CustomerId}");

        if (order.Items.Count == 0)
        {
            return new(false, "NoItems", 0m);
        }

        if (string.IsNullOrWhiteSpace(order.CustomerEmail))
        {
            return new(false, "InvalidEmail", 0m);
        }

        if (!inventory.HasStock(order))
        {
            return new(false, "OutOfStock", 0m);
        }

        var total = order.Items.Sum(item => item.Quantity * item.UnitPrice);
        if (order.IsPremium)
        {
            total *= 0.9m;
        }

        emailService.Send(order.CustomerEmail);
        return new(true, "Processed", total);
    }
}

public sealed class OrderValidator
{
    public OrderValidationResult Validate(Order order)
    {
        if (string.IsNullOrWhiteSpace(order.CustomerEmail))
        {
            return new(false, "Email is required");
        }

        if (order.Items.Count == 0)
        {
            return new(false, "Order must have items");
        }

        if (order.Items.Any(item => item.UnitPrice <= 0))
        {
            return new(false, "Price must be positive");
        }

        if (order.Items.Any(item => item.Quantity <= 0))
        {
            return new(false, "Quantity must be positive");
        }

        return new(true, null);
    }
}
