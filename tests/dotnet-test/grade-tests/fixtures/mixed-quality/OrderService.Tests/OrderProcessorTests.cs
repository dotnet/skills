using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OrderService.Tests;

[TestClass]
public sealed class OrderProcessorTests
{
    // ============================================================
    // STRONG TEST: clear AAA, meaningful equality + state assertions
    // Expected grade: A (90–100)
    // ============================================================
    [TestMethod]
    public void PlaceOrder_ValidItems_AssignsOrderIdAndPersistsOrder()
    {
        // Arrange
        var repository = new InMemoryOrderRepository();
        var processor = new OrderProcessor(repository);
        var items = new[] { new OrderItem("SKU-1", 2), new OrderItem("SKU-2", 1) };

        // Act
        Order placed = processor.PlaceOrder(customerId: 42, items: items);

        // Assert
        Assert.IsNotNull(placed.Id);
        Assert.AreEqual(42, placed.CustomerId);
        Assert.AreEqual(2, placed.Items.Count);
        Assert.AreEqual("SKU-1", placed.Items[0].Sku);
        Assert.IsTrue(repository.Contains(placed.Id), "order should be saved to repository");
    }

    // ============================================================
    // STRONG TEST: exception assertion with specific type + message check
    // Expected grade: A (90–100)
    // ============================================================
    [TestMethod]
    public void PlaceOrder_EmptyItems_ThrowsArgumentException()
    {
        var processor = new OrderProcessor(new InMemoryOrderRepository());

        ArgumentException ex = Assert.ThrowsException<ArgumentException>(
            () => processor.PlaceOrder(customerId: 42, items: Array.Empty<OrderItem>()));

        Assert.AreEqual("items", ex.ParamName);
    }

    // ============================================================
    // WEAK TEST: only IsNotNull, no value verification
    // Expected grade: C (70–79)
    // ============================================================
    [TestMethod]
    public void GetOrderById_ExistingId_ReturnsOrder()
    {
        var repository = new InMemoryOrderRepository();
        var processor = new OrderProcessor(repository);
        processor.PlaceOrder(customerId: 7, items: new[] { new OrderItem("SKU-1", 1) });

        var result = processor.GetOrderById("ORD-1");

        Assert.IsNotNull(result);
    }

    // ============================================================
    // BAD TEST: no assertions at all
    // Expected grade: F (0–59)
    // ============================================================
    [TestMethod]
    public void CancelOrder_ExistingOrder_Works()
    {
        var processor = new OrderProcessor(new InMemoryOrderRepository());
        var order = processor.PlaceOrder(customerId: 1, items: new[] { new OrderItem("SKU-1", 1) });
        processor.CancelOrder(order.Id);
    }

    // ============================================================
    // BAD TEST: self-referential / tautological assertion
    // Expected grade: D (60–69)
    // ============================================================
    [TestMethod]
    public void SerializeOrderId_RoundTrip_ReturnsSameId()
    {
        var processor = new OrderProcessor(new InMemoryOrderRepository());
        var id = "ORD-42";

        var serialized = processor.SerializeOrderId(id);
        var roundTripped = processor.DeserializeOrderId(serialized);

        Assert.AreEqual(id, roundTripped);
    }

    // ============================================================
    // BAD TEST: Thread.Sleep used for synchronization (flakiness anti-pattern)
    // Expected grade: D (60–69)
    // ============================================================
    [TestMethod]
    public void PlaceOrder_LongRunning_CompletesEventually()
    {
        var processor = new OrderProcessor(new InMemoryOrderRepository());

        processor.PlaceOrderAsync(customerId: 1, items: new[] { new OrderItem("SKU-1", 1) });
        Thread.Sleep(2000);

        Assert.IsTrue(processor.HasPendingOrders == false);
    }

    // ============================================================
    // BAD TEST: poor name + magic values + swallowed exception
    // Expected grade: F (0–59)
    // ============================================================
    [TestMethod]
    public void Test1()
    {
        try
        {
            var processor = new OrderProcessor(new InMemoryOrderRepository());
            var result = processor.PlaceOrder(42, new[] { new OrderItem("X", 99) });
            Assert.AreEqual(42, result.CustomerId);
        }
        catch (Exception)
        {
            // silently ignore — the order is allowed to fail
        }
    }
}
