using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WeakTests;

[TestClass]
public class ShoppingCartTests
{
    // Anti-pattern: assertion-free test
    [TestMethod]
    public void AddItem_Works()
    {
        var cart = new ShoppingCart();
        cart.AddItem("SKU-001", 2, 9.99m);
    }

    // Anti-pattern: trivial assertion only (IsNotNull but no value check)
    [TestMethod]
    public void AddItem_ItemIsAdded()
    {
        var cart = new ShoppingCart();
        cart.AddItem("SKU-001", 1, 5.00m);
        Assert.IsNotNull(cart.Items);
    }

    // Anti-pattern: tautological assertion (comparing value to itself)
    [TestMethod]
    public void GetTotal_ReturnsValue()
    {
        var cart = new ShoppingCart();
        cart.AddItem("SKU-001", 2, 10.00m);
        var total = cart.GetTotal();
        Assert.AreEqual(total, total);
    }

    // Anti-pattern: catch-and-swallow exception test
    [TestMethod]
    public void AddItem_NegativePrice_Throws()
    {
        var cart = new ShoppingCart();
        try
        {
            cart.AddItem("SKU-001", 1, -5.00m);
        }
        catch (Exception)
        {
            return;
        }
    }

    // Missing: no tests for RemoveItem
    // Missing: no tests for GetTotalWithDiscount
    // Missing: no tests for AddItem with duplicate product ID (quantity merge)
    // Missing: no boundary tests for discount (0%, 100%)
    // Missing: no negative tests for null/empty productId, zero quantity

    // Shallow: only equality assertions, no state or structural checks
    [TestMethod]
    public void ItemCount_AfterAdd()
    {
        var cart = new ShoppingCart();
        cart.AddItem("SKU-001", 1, 5.00m);
        Assert.AreEqual(1, cart.ItemCount);
    }

    // Anti-pattern: Thread.Sleep in test
    [TestMethod]
    public void GetTotal_WithMultipleItems()
    {
        var cart = new ShoppingCart();
        cart.AddItem("SKU-001", 1, 10.00m);
        cart.AddItem("SKU-002", 3, 5.00m);
        Thread.Sleep(100); // unnecessary delay
        Assert.AreEqual(25.00m, cart.GetTotal());
    }
}
