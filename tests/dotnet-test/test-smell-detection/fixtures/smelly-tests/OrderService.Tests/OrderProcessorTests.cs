using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OrderService.Tests;

[TestClass]
public sealed class OrderProcessorTests
{
    private FakeDatabase _db = null!;
    private FakeEmailSender _email = null!;
    private FakeInventory _inventory = null!;
    private FakeLogger _logger = null!;

    [TestInitialize]
    public void Setup()
    {
        _db = new FakeDatabase();
        _email = new FakeEmailSender();
        _inventory = new FakeInventory();
        _logger = new FakeLogger();
    }

    [TestMethod]
    public void ProcessOrder_SetsCorrectStatus()
    {
        var processor = new OrderProcessor(_db, _email, _inventory);
        var order = new Order { Items = { new OrderItem("SKU-1", 2) } };

        var result = processor.ProcessOrder(order);

        if (result.TotalAmount > 100)
        {
            Assert.AreEqual("PremiumProcessed", result.Status);
        }
        else
        {
            Assert.AreEqual("StandardProcessed", result.Status);
        }
    }

    [TestMethod]
    public void ProcessOrder_CompletesWithoutError()
    {
        var processor = new OrderProcessor(_db, _email, _inventory);
        var order = new Order { Items = { new OrderItem("SKU-1", 1) } };
        processor.ProcessOrder(order);
    }

    [TestMethod]
    public void OrderProcessor_FullWorkflow_Succeeds()
    {
        var processor = new OrderProcessor(_db, _email, _inventory);
        var order = new Order { Items = { new OrderItem("SKU-1", 2) } };

        processor.ValidateOrder(order);
        processor.CalculateTotal(order);
        processor.ApplyDiscount(order, "SAVE10");
        processor.ReserveInventory(order);
        processor.ProcessPayment(order, new CreditCard("4111111111111111"));
        processor.SendConfirmation(order);
        processor.UpdateOrderHistory(order);

        Assert.AreEqual("Completed", order.Status);
    }

    [TestMethod]
    public void CalculateTotal_ReturnsCorrectAmount()
    {
        var processor = new OrderProcessor(_db, _email, _inventory);
        var order = new Order
        {
            Items =
            {
                new OrderItem("SKU-1", 3),
                new OrderItem("SKU-2", 1)
            }
        };

        processor.CalculateTotal(order);

        Assert.AreEqual(247.50m, order.TotalAmount);
        Assert.AreEqual(22.28m, order.TaxAmount);
        Assert.AreEqual(269.78m, order.GrandTotal);
    }

    [TestMethod]
    public void ProcessOrder_AsyncNotification_IsSent()
    {
        var processor = new OrderProcessor(_db, _email, _inventory);
        var order = new Order { Items = { new OrderItem("SKU-1", 1) } };

        processor.ProcessOrderAsync(order);

        Thread.Sleep(2000);

        Assert.IsTrue(_email.WasNotificationSent(order.Id));
    }

    [TestMethod]
    public void ProcessOrder_EmptyOrder_ThrowsValidationError()
    {
        var processor = new OrderProcessor(_db, _email, _inventory);
        var order = new Order();

        try
        {
            processor.ProcessOrder(order);
            Assert.Fail("Expected an exception but none was thrown");
        }
        catch (ValidationException ex)
        {
            Assert.AreEqual("Order must contain at least one item", ex.Message);
        }
    }

    [TestMethod]
    public void GetOrderSummary_ReturnsFormattedString()
    {
        var processor = new OrderProcessor(_db, _email, _inventory);
        var order = new Order
        {
            Id = "ORD-001",
            Items = { new OrderItem("SKU-1", 1) },
            TotalAmount = 99.99m
        };

        var summary = processor.GetOrderSummary(order);

        Assert.AreEqual("Order ORD-001: 1 item(s), Total: $99.99", summary.ToString());
    }

    [TestMethod]
    public void ImportOrders_FromCsv_ParsesCorrectly()
    {
        var processor = new OrderProcessor(_db, _email, _inventory);

        var orders = processor.ImportOrders(
            File.ReadAllText(@"C:\TestData\orders.csv"));

        Assert.AreEqual(5, orders.Count);
    }

    [TestMethod]
    public void ValidateOrder_NullOrder_ThrowsArgumentNullException()
    {
        var processor = new OrderProcessor(_db, _email, _inventory);

        var ex = Assert.ThrowsExactly<ArgumentNullException>(
            () => processor.ValidateOrder(null!));

        Assert.AreEqual("order", ex.ParamName);
    }
}
