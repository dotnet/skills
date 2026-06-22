using NUnit.Framework;
using OrderService;

namespace OrderService.Tests;

[TestFixture]
public sealed class OrderProcessorTests
{
    private OrderProcessor _processor = null!;
    private FakeOrderRepository _repository = null!;
    private FakeEmailService _emailService = null!;

    [SetUp]
    public void Setup()
    {
        _repository = new FakeOrderRepository();
        _emailService = new FakeEmailService();
        _processor = new OrderProcessor(_repository, _emailService);
    }

    [Test]
    public async Task PlaceOrder_ValidInput_ReturnsConfirmedOrder()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 9.99m, Quantity = 2 }
        };

        var order = await _processor.PlaceOrderAsync("customer@example.com", items);

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Confirmed));
        Assert.That(order.CustomerEmail, Is.EqualTo("customer@example.com"));
        Assert.That(order.Items, Has.Count.EqualTo(1));
    }

    [Test]
    public void CalculateDiscount_NegativeTotal_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _processor.CalculateDiscount(-1m, 100));
    }

    [Test]
    public async Task CancelOrder_ExistingPendingOrder_SetsCancelledStatus()
    {
        var order = new Order { Id = 1, Status = OrderStatus.Pending };
        _repository.Orders[1] = order;

        await _processor.CancelOrderAsync(1);

        Assert.That(order.Status, Is.EqualTo(OrderStatus.Cancelled));
    }

    [Test]
    public void PlaceOrder_NullEmail_ThrowsArgumentException()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 5.00m, Quantity = 1 }
        };

        Assert.ThrowsAsync<ArgumentException>(
            () => _processor.PlaceOrderAsync(null!, items));
    }

    [Test]
    public void CalculateDiscount_HighPoints_Returns15Percent()
    {
        var discount = _processor.CalculateDiscount(100.00m, 1000);
        Assert.That(discount, Is.EqualTo(15.00m));
    }

    [Test]
    public void PlaceOrder_ZeroQuantity_ThrowsArgumentException()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 5.00m, Quantity = 0 }
        };

        Assert.ThrowsAsync<ArgumentException>(
            () => _processor.PlaceOrderAsync("user@test.com", items));
    }

    [Test]
    public async Task PlaceOrder_MultipleItems_CalculatesTotalCorrectly()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 10.00m, Quantity = 2 },
            new() { ProductName = "Gadget", Price = 25.00m, Quantity = 1 }
        };

        var order = await _processor.PlaceOrderAsync("buyer@test.com", items);

        Assert.That(order.Total, Is.EqualTo(45.00m));
    }

    [Test]
    public void Constructor_NullRepository_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new OrderProcessor(null!, _emailService));
    }

    [Test]
    public void CalculateDiscount_ZeroTotal_ReturnsZeroRegardlessOfPoints()
    {
        var discount = _processor.CalculateDiscount(0m, 1000);
        Assert.That(discount, Is.EqualTo(0m));
    }

    [Test]
    public void PlaceOrder_EmptyEmail_ThrowsArgumentException()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 5.00m, Quantity = 1 }
        };

        Assert.ThrowsAsync<ArgumentException>(
            () => _processor.PlaceOrderAsync("", items));
    }

    [Test]
    public void CancelOrder_ShippedOrder_ThrowsInvalidOperationException()
    {
        var order = new Order { Id = 1, Status = OrderStatus.Shipped };
        _repository.Orders[1] = order;

        Assert.ThrowsAsync<InvalidOperationException>(
            () => _processor.CancelOrderAsync(1));
    }

    [Test]
    public void CalculateDiscount_Exactly500Points_Returns10Percent()
    {
        var discount = _processor.CalculateDiscount(200.00m, 500);
        Assert.That(discount, Is.EqualTo(20.00m));
    }

    [Test]
    public async Task PlaceOrder_SendsConfirmationEmail()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 5.00m, Quantity = 1 }
        };

        await _processor.PlaceOrderAsync("user@test.com", items);

        Assert.That(_emailService.SentEmails, Is.Not.Empty);
        Assert.That(_emailService.SentEmails[0], Is.EqualTo("user@test.com"));
    }

    [Test]
    public void CalculateDiscount_NegativePoints_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _processor.CalculateDiscount(100m, -1));
    }

    [Test]
    public void PlaceOrder_InvalidEmailFormat_ThrowsArgumentException()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 5.00m, Quantity = 1 }
        };

        Assert.ThrowsAsync<ArgumentException>(
            () => _processor.PlaceOrderAsync("not-an-email", items));
    }

    [Test]
    public void CalculateDiscount_MediumPoints_Returns10Percent()
    {
        var discount = _processor.CalculateDiscount(100.00m, 500);
        Assert.That(discount, Is.EqualTo(10.00m));
    }

    [Test]
    public void PlaceOrder_EmptyItemList_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(
            () => _processor.PlaceOrderAsync("user@test.com", new List<OrderItem>()));
    }

    [Test]
    public void Constructor_NullEmailService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new OrderProcessor(_repository, null!));
    }

    [Test]
    public void CalculateDiscount_LowPoints_Returns5Percent()
    {
        var discount = _processor.CalculateDiscount(100.00m, 100);
        Assert.That(discount, Is.EqualTo(5.00m));
    }

    [Test]
    public void CancelOrder_NonExistentOrder_ThrowsInvalidOperationException()
    {
        Assert.ThrowsAsync<InvalidOperationException>(
            () => _processor.CancelOrderAsync(999));
    }

    [Test]
    public void PlaceOrder_NegativePrice_ThrowsArgumentException()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = -1.00m, Quantity = 1 }
        };

        Assert.ThrowsAsync<ArgumentException>(
            () => _processor.PlaceOrderAsync("user@test.com", items));
    }

    [Test]
    public void CalculateDiscount_ZeroPoints_ReturnsZero()
    {
        var discount = _processor.CalculateDiscount(100.00m, 0);
        Assert.That(discount, Is.EqualTo(0m));
    }

    [Test]
    public void PlaceOrder_NullItemList_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(
            () => _processor.PlaceOrderAsync("user@test.com", null!));
    }
}

// --- Test doubles ---

internal sealed class FakeOrderRepository : IOrderRepository
{
    public Dictionary<int, Order> Orders { get; } = new();

    public Task<Order?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        Orders.TryGetValue(id, out var order);
        return Task.FromResult(order);
    }

    public Task SaveAsync(Order order, CancellationToken ct = default)
    {
        Orders[order.Id] = order;
        return Task.CompletedTask;
    }
}

internal sealed class FakeEmailService : IEmailService
{
    public List<string> SentEmails { get; } = [];

    public Task SendConfirmationAsync(string email, int orderId, CancellationToken ct = default)
    {
        SentEmails.Add(email);
        return Task.CompletedTask;
    }
}
