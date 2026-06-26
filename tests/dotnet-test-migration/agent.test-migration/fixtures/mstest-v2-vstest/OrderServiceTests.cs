using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OrderTests;

[TestClass]
public class OrderServiceTests
{
    [TestMethod]
    public void CreateOrder_ValidInput_ReturnsOrder()
    {
        var service = new OrderService();
        var order = service.Create("item-1", 2);
        Assert.IsNotNull(order);
        Assert.AreEqual("item-1", order.ItemId);
        Assert.AreEqual(2, order.Quantity);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void CreateOrder_InvalidQuantity_Throws(int quantity)
    {
        var service = new OrderService();
        Assert.ThrowsException<ArgumentException>(() => service.Create("item-1", quantity));
    }
}

public class OrderService
{
    public Order Create(string itemId, int quantity)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive", nameof(quantity));
        return new Order { ItemId = itemId, Quantity = quantity };
    }
}

public class Order
{
    public string ItemId { get; set; } = "";
    public int Quantity { get; set; }
}
