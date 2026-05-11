using Microsoft.VisualStudio.TestTools.UnitTesting;
using OrderService;

namespace OrderService.Tests;

[TestClass]
public sealed class OrderProcessorTests
{
    private OrderProcessor _processor = null!;
    private FakeOrderRepository _repository = null!;
    private FakeEmailService _emailService = null!;

    [TestInitialize]
    public void Setup()
    {
        _repository = new FakeOrderRepository();
        _emailService = new FakeEmailService();
        _processor = new OrderProcessor(_repository, _emailService);
    }

    // Already tagged — agent must preserve these and not duplicate them
    [TestMethod]
    [TestCategory("positive")]
    [TestCategory("critical-path")]
    public async Task PlaceOrder_ValidInput_ReturnsConfirmedOrder()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 9.99m, Quantity = 2 }
        };

        var order = await _processor.PlaceOrderAsync("customer@example.com", items);

        Assert.AreEqual(OrderStatus.Confirmed, order.Status);
        Assert.AreEqual("customer@example.com", order.CustomerEmail);
        Assert.AreEqual(1, order.Items.Count);
    }

    // Already tagged — agent must preserve
    [TestMethod]
    [TestCategory("negative")]
    public async Task PlaceOrder_NullEmail_ThrowsArgumentException()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 5.00m, Quantity = 1 }
        };

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _processor.PlaceOrderAsync(null!, items));
    }

    // Already tagged — agent must preserve
    [TestMethod]
    [TestCategory("negative")]
    [TestCategory("boundary")]
    public async Task PlaceOrder_ZeroQuantity_ThrowsArgumentException()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 5.00m, Quantity = 0 }
        };

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _processor.PlaceOrderAsync("user@test.com", items));
    }

    // --- Untagged tests below — agent should add traits ---

    [TestMethod]
    public void CalculateDiscount_NegativeTotal_ThrowsArgumentOutOfRangeException()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => _processor.CalculateDiscount(-1m, 100));
    }

    [TestMethod]
    public async Task CancelOrder_ExistingPendingOrder_SetsCancelledStatus()
    {
        var order = new Order { Id = 1, Status = OrderStatus.Pending };
        _repository.Orders[1] = order;

        await _processor.CancelOrderAsync(1);

        Assert.AreEqual(OrderStatus.Cancelled, order.Status);
    }

    [TestMethod]
    public void CalculateDiscount_HighPoints_Returns15Percent()
    {
        var discount = _processor.CalculateDiscount(100.00m, 1000);
        Assert.AreEqual(15.00m, discount);
    }

    [TestMethod]
    public async Task PlaceOrder_MultipleItems_CalculatesTotalCorrectly()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 10.00m, Quantity = 2 },
            new() { ProductName = "Gadget", Price = 25.00m, Quantity = 1 }
        };

        var order = await _processor.PlaceOrderAsync("buyer@test.com", items);

        Assert.AreEqual(45.00m, order.Total);
    }

    [TestMethod]
    public void Constructor_NullRepository_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new OrderProcessor(null!, _emailService));
    }

    [TestMethod]
    public void CalculateDiscount_ZeroTotal_ReturnsZeroRegardlessOfPoints()
    {
        var discount = _processor.CalculateDiscount(0m, 1000);
        Assert.AreEqual(0m, discount);
    }

    [TestMethod]
    public async Task PlaceOrder_EmptyEmail_ThrowsArgumentException()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 5.00m, Quantity = 1 }
        };

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _processor.PlaceOrderAsync("", items));
    }

    [TestMethod]
    public async Task CancelOrder_ShippedOrder_ThrowsInvalidOperationException()
    {
        var order = new Order { Id = 1, Status = OrderStatus.Shipped };
        _repository.Orders[1] = order;

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _processor.CancelOrderAsync(1));
    }

    [TestMethod]
    public void CalculateDiscount_Exactly500Points_Returns10Percent()
    {
        var discount = _processor.CalculateDiscount(200.00m, 500);
        Assert.AreEqual(20.00m, discount);
    }

    [TestMethod]
    public async Task PlaceOrder_SendsConfirmationEmail()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = 5.00m, Quantity = 1 }
        };

        await _processor.PlaceOrderAsync("user@test.com", items);

        Assert.IsTrue(_emailService.SentEmails.Count > 0);
        Assert.AreEqual("user@test.com", _emailService.SentEmails[0]);
    }

    [TestMethod]
    public void CalculateDiscount_NegativePoints_ThrowsArgumentOutOfRangeException()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => _processor.CalculateDiscount(100m, -1));
    }

    [TestMethod]
    public async Task PlaceOrder_NegativePrice_ThrowsArgumentException()
    {
        var items = new List<OrderItem>
        {
            new() { ProductName = "Widget", Price = -1.00m, Quantity = 1 }
        };

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => _processor.PlaceOrderAsync("user@test.com", items));
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
