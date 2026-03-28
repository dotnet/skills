using System.Text.RegularExpressions;

namespace OrderService;

public sealed partial class OrderProcessor
{
    private readonly IOrderRepository _repository;
    private readonly IEmailService _emailService;

    public OrderProcessor(IOrderRepository repository, IEmailService emailService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
    }

    public async Task<Order> PlaceOrderAsync(string customerEmail, List<OrderItem> items, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(customerEmail))
            throw new ArgumentException("Email is required.", nameof(customerEmail));

        if (!EmailRegex().IsMatch(customerEmail))
            throw new ArgumentException("Invalid email format.", nameof(customerEmail));

        if (items is null || items.Count == 0)
            throw new ArgumentException("At least one item is required.", nameof(items));

        if (items.Any(i => i.Quantity <= 0))
            throw new ArgumentException("All item quantities must be positive.", nameof(items));

        if (items.Any(i => i.Price < 0))
            throw new ArgumentException("Item prices cannot be negative.", nameof(items));

        var order = new Order
        {
            CustomerEmail = customerEmail,
            Items = items,
            Status = OrderStatus.Confirmed
        };

        await _repository.SaveAsync(order, ct);
        await _emailService.SendConfirmationAsync(customerEmail, order.Id, ct);

        return order;
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _repository.GetByIdAsync(orderId, ct)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");

        if (order.Status == OrderStatus.Shipped)
            throw new InvalidOperationException("Cannot cancel a shipped order.");

        order.Status = OrderStatus.Cancelled;
        await _repository.SaveAsync(order, ct);
    }

    public decimal CalculateDiscount(decimal total, int loyaltyPoints)
    {
        if (total < 0) throw new ArgumentOutOfRangeException(nameof(total));
        if (loyaltyPoints < 0) throw new ArgumentOutOfRangeException(nameof(loyaltyPoints));

        return loyaltyPoints switch
        {
            >= 1000 => total * 0.15m,
            >= 500 => total * 0.10m,
            >= 100 => total * 0.05m,
            _ => 0m
        };
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();
}

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(int id, CancellationToken ct = default);
    Task SaveAsync(Order order, CancellationToken ct = default);
}

public interface IEmailService
{
    Task SendConfirmationAsync(string email, int orderId, CancellationToken ct = default);
}
