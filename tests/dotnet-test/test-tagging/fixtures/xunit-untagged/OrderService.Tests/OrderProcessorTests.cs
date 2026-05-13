using Xunit;
using OrderService;

namespace OrderService.Tests;

public sealed class OrderProcessorTests : IDisposable
{
    private readonly OrderProcessor _processor;
    private readonly FakeOrderRepository _repository;
    private readonly FakeEmailService _emailService;

    public OrderProcessorTests()
    {
        _repository = new FakeOrderRepository();
        _emailService = new FakeEmailService();
        _processor = new OrderProcessor(_repository, _emailService);
    }

    public void Dispose() { }

    [Fact]
    public async Task PlaceOrder_ValidInput_ReturnsConfirmedOrder()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 9.99m, Quantity = 2 }
        };

        var order = await _processor.PlaceOrderAsync("customer@example.com", items);

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal("customer@example.com", order.CustomerEmail);
        Assert.Single(order.Items);
    }

    [Fact]
    public void CalculateDiscount_NegativeTotal_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _processor.CalculateDiscount(-1m, 100));
    }

    [Fact]
    public async Task CancelOrder_ExistingPendingOrder_SetsCancelledStatus()
    {
        var order = new Order { Id = 1, Status = OrderStatus.Pending };
        _repository.Orders[1] = order;

        await _processor.CancelOrderAsync(1);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public async Task PlaceOrder_NullEmail_ThrowsArgumentException()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 5.00m, Quantity = 1 }
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _processor.PlaceOrderAsync(null!, items));
    }

    [Fact]
    public void CalculateDiscount_HighPoints_Returns15Percent()
    {
        var discount = _processor.CalculateDiscount(100.00m, 1000);
        Assert.Equal(15.00m, discount);
    }

    [Fact]
    public async Task PlaceOrder_ZeroQuantity_ThrowsArgumentException()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 5.00m, Quantity = 0 }
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _processor.PlaceOrderAsync("user@test.com", items));
    }

    [Fact]
    public async Task PlaceOrder_MultipleItems_CalculatesTotalCorrectly()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 10.00m, Quantity = 2 },
            new() { ProductName = "Gadget", Price = 25.00m, Quantity = 1 }
        };

        var order = await _processor.PlaceOrderAsync("buyer@test.com", items);

        Assert.Equal(45.00m, order.Total);
    }

    [Fact]
    public void Constructor_NullRepository_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new OrderProcessor(null!, _emailService));
    }

    [Fact]
    public void CalculateDiscount_ZeroTotal_ReturnsZeroRegardlessOfPoints()
    {
        var discount = _processor.CalculateDiscount(0m, 1000);
        Assert.Equal(0m, discount);
    }

    [Fact]
    public async Task PlaceOrder_EmptyEmail_ThrowsArgumentException()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 5.00m, Quantity = 1 }
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _processor.PlaceOrderAsync("", items));
    }

    [Fact]
    public async Task CancelOrder_ShippedOrder_ThrowsInvalidOperationException()
    {
        var order = new Order { Id = 1, Status = OrderStatus.Shipped };
        _repository.Orders[1] = order;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _processor.CancelOrderAsync(1));
    }

    [Fact]
    public void CalculateDiscount_Exactly500Points_Returns10Percent()
    {
        var discount = _processor.CalculateDiscount(200.00m, 500);
        Assert.Equal(20.00m, discount);
    }

    [Fact]
    public async Task PlaceOrder_SendsConfirmationEmail()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 5.00m, Quantity = 1 }
        };

        await _processor.PlaceOrderAsync("user@test.com", items);

        Assert.NotEmpty(_emailService.SentEmails);
        Assert.Equal("user@test.com", _emailService.SentEmails[0]);
    }

    [Fact]
    public void CalculateDiscount_NegativePoints_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _processor.CalculateDiscount(100m, -1));
    }

    [Fact]
    public async Task PlaceOrder_InvalidEmailFormat_ThrowsArgumentException()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 5.00m, Quantity = 1 }
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _processor.PlaceOrderAsync("not-an-email", items));
    }

    [Fact]
    public void CalculateDiscount_MediumPoints_Returns10Percent()
    {
        var discount = _processor.CalculateDiscount(100.00m, 500);
        Assert.Equal(10.00m, discount);
    }

    [Fact]
    public async Task PlaceOrder_EmptyItemList_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _processor.PlaceOrderAsync("user@test.com", new List<OrderItem>()));
    }

    [Fact]
    public void Constructor_NullEmailService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new OrderProcessor(_repository, null!));
    }

    [Fact]
    public void CalculateDiscount_LowPoints_Returns5Percent()
    {
        var discount = _processor.CalculateDiscount(100.00m, 100);
        Assert.Equal(5.00m, discount);
    }

    [Fact]
    public async Task CancelOrder_NonExistentOrder_ThrowsInvalidOperationException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _processor.CancelOrderAsync(999));
    }

    [Fact]
    public async Task PlaceOrder_NegativePrice_ThrowsArgumentException()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = -1.00m, Quantity = 1 }
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => _processor.PlaceOrderAsync("user@test.com", items));
    }

    [Fact]
    public void CalculateDiscount_ZeroPoints_ReturnsZero()
    {
        var discount = _processor.CalculateDiscount(100.00m, 0);
        Assert.Equal(0m, discount);
    }

    [Fact]
    public async Task PlaceOrder_NullItemList_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
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
