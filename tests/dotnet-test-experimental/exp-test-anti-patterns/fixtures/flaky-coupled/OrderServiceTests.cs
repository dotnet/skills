using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;

namespace FlakyCoupled.Tests;

[TestClass]
public class OrderServiceTests
{
    private static List<string> _processedOrders = new();

    [TestInitialize]
    public void Setup()
    {
        // Note: _processedOrders is never cleared between tests
    }

    [TestMethod]
    public void Test1()
    {
        var service = new OrderService();
        var order = service.CreateOrder("item-1", 2);
        _processedOrders.Add(order.Id);
        Assert.IsNotNull(order);
    }

    [TestMethod]
    public void Test2()
    {
        Assert.AreEqual(1, _processedOrders.Count);
        var service = new OrderService();
        service.ProcessOrder(_processedOrders[0]);
    }

    [TestMethod]
    public void ProcessOrder_SetsTimestamp()
    {
        var service = new OrderService();
        var order = service.CreateOrder("item-2", 1);
        service.ProcessOrder(order.Id);

        Thread.Sleep(2000);

        var processed = service.GetOrder(order.Id);
        Assert.IsTrue(processed.ProcessedAt.Value.Day == DateTime.Now.Day);
    }

    [TestMethod]
    public void CreateOrder_InternalState_IsCorrect()
    {
        var service = new OrderService();
        var order = service.CreateOrder("item-3", 5);

        // Use reflection to verify internal state
        var field = typeof(OrderService).GetField("_orders", BindingFlags.NonPublic | BindingFlags.Instance);
        var orders = (Dictionary<string, Order>)field.GetValue(service);
        Assert.AreEqual(1, orders.Count);
    }

    [TestMethod]
    public void CreateOrder_MultipleItems_Test()
    {
        var service = new OrderService();
        var order1 = service.CreateOrder("item-a", 1);
        Assert.IsNotNull(order1);
        Assert.IsNotNull(order1.Id);
        var order2 = service.CreateOrder("item-b", 2);
        Assert.IsNotNull(order2);
        Assert.IsNotNull(order2.Id);
        var order3 = service.CreateOrder("item-c", 3);
        Assert.IsNotNull(order3);
        Assert.IsNotNull(order3.Id);
        Assert.AreNotEqual(order1.Id, order2.Id);
        Assert.AreNotEqual(order2.Id, order3.Id);
        Assert.AreNotEqual(order1.Id, order3.Id);
        var all = service.GetAllOrders();
        Assert.AreEqual(3, all.Count);
        Assert.IsTrue(all.Any(o => o.ItemName == "item-a"));
        Assert.IsTrue(all.Any(o => o.ItemName == "item-b"));
        Assert.IsTrue(all.Any(o => o.ItemName == "item-c"));
        Assert.IsTrue(all.All(o => o.Quantity > 0));
        Assert.IsTrue(all.Sum(o => o.Quantity) == 6);
        Console.WriteLine($"Created {all.Count} orders successfully");
    }

    [TestMethod]
    public void ProcessOrder_InvalidId_Throws()
    {
        var service = new OrderService();
        Assert.ThrowsException<Exception>(() => service.ProcessOrder("nonexistent"));
    }
}
