using Microsoft.VisualStudio.TestTools.UnitTesting;
using MyApp;
using NSubstitute;

namespace MyApp.Tests;

[TestClass]
public sealed class OrderServiceTests
{
    [TestMethod]
    public void ProcessOrder_WithValidItems_ReturnsSuccess()
    {
        var repo = Substitute.For<IOrderRepository>();
        var service = new OrderService(repo);
        var order = new Order
        {
            Id = 1,
            Items = new List<OrderItem>
            {
                new() { Name = "Widget", Price = 10m, Quantity = 2 }
            }
        };

        var result = service.ProcessOrder(order);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(20m, result.Total);
    }

    [TestMethod]
    public void ProcessOrder_WithNullOrder_ThrowsArgumentNull()
    {
        var repo = Substitute.For<IOrderRepository>();
        var service = new OrderService(repo);

        Assert.ThrowsException<ArgumentNullException>(() => service.ProcessOrder(null!));
    }
}
