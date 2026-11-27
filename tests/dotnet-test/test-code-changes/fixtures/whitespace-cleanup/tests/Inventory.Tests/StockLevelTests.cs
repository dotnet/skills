using Inventory;

namespace Inventory.Tests;

public class StockLevelTests
{
    [Fact]
    public void Receive_AddsToOnHand()
    {
        var stock = new StockLevel();

        stock.Receive("PEN-1", 5);
        stock.Receive("pen-1", 3);

        Assert.Equal(8, stock.OnHand("PEN-1"));
    }

    [Fact]
    public void TryShip_MoreThanOnHand_LeavesStockUnchanged()
    {
        var stock = new StockLevel();
        stock.Receive("PEN-1", 2);

        Assert.False(stock.TryShip("PEN-1", 3));
        Assert.Equal(2, stock.OnHand("PEN-1"));
    }

    [Fact]
    public void SuggestedOrder_AboveReorderPoint_IsZero()
    {
        Assert.Equal(0, ReorderPolicy.SuggestedOrder(onHand: 12, reorderPoint: 10, targetLevel: 40));
    }

    [Fact]
    public void SuggestedOrder_AtReorderPoint_FillsToTarget()
    {
        Assert.Equal(30, ReorderPolicy.SuggestedOrder(onHand: 10, reorderPoint: 10, targetLevel: 40));
    }
}
