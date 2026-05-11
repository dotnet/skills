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

    // Smell: Conditional Test Logic — uses if/else inside test
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

    // Smell: Assertion-Free Test — no assertions at all
    [TestMethod]
    public void ProcessOrder_CompletesWithoutError()
    {
        var processor = new OrderProcessor(_db, _email, _inventory);
        var order = new Order { Items = { new OrderItem("SKU-1", 1) } };
        processor.ProcessOrder(order);
    }

    // Smell: Eager Test — calls many distinct production methods
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

    // Smell: Magic Number Test — unexplained numeric literals
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

    // Smell: Sleepy Test — uses Thread.Sleep
    [TestMethod]
    public void ProcessOrder_AsyncNotification_IsSent()
    {
        var processor = new OrderProcessor(_db, _email, _inventory);
        var order = new Order { Items = { new OrderItem("SKU-1", 1) } };

        processor.ProcessOrderAsync(order);

        Thread.Sleep(2000);

        Assert.IsTrue(_email.WasNotificationSent(order.Id));
    }

    // Smell: Exception Handling in Test — try/catch instead of Assert.ThrowsException
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

    // Smell: Sensitive Equality — uses ToString() for comparison
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

    // Smell: Mystery Guest — reads from file system
    [TestMethod]
    public void ImportOrders_FromCsv_ParsesCorrectly()
    {
        var processor = new OrderProcessor(_db, _email, _inventory);

        var orders = processor.ImportOrders(
            File.ReadAllText(@"C:\TestData\orders.csv"));

        Assert.AreEqual(5, orders.Count);
    }

    // Smell: General Fixture — _logger is initialized in Setup but never used by any test
    // (All tests above use _db, _email, _inventory but none use _logger)

    // Clean test for contrast — this one has no smells
    [TestMethod]
    public void ValidateOrder_NullOrder_ThrowsArgumentNullException()
    {
        var processor = new OrderProcessor(_db, _email, _inventory);

        var ex = Assert.ThrowsException<ArgumentNullException>(
            () => processor.ValidateOrder(null!));

        Assert.AreEqual("order", ex.ParamName);
    }
}
