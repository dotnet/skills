using System.Diagnostics;

namespace StaticHeavy.Services;

public class OrderProcessor
{
    private readonly ILogger<OrderProcessor> _logger;

    public OrderProcessor(ILogger<OrderProcessor> logger)
    {
        _logger = logger;
    }

    public Order CreateOrder(string customerId, decimal amount)
    {
        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            CustomerId = customerId,
            Amount = amount,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };

        var receipt = $"Order {order.Id} created at {DateTime.Now}";
        File.WriteAllText(Path.Combine(Path.GetTempPath(), $"{order.Id}.txt"), receipt);

        _logger.LogInformation("Order created: {OrderId}", order.Id);
        return order;
    }

    public bool IsExpired(Order order)
    {
        return DateTime.UtcNow > order.ExpiresAt;
    }

    public void ArchiveOrder(Order order)
    {
        var archiveDir = Path.Combine(Environment.GetEnvironmentVariable("ARCHIVE_PATH") ?? "/tmp/archive", "orders");
        if (!Directory.Exists(archiveDir))
        {
            Directory.CreateDirectory(archiveDir);
        }

        var json = System.Text.Json.JsonSerializer.Serialize(order);
        File.WriteAllText(Path.Combine(archiveDir, $"{order.Id}.json"), json);

        Console.WriteLine($"Archived order {order.Id}");
    }
}

public class Order
{
    public string Id { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
