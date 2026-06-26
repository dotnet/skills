using Xunit;

namespace Inventory.Tests;

public sealed class InventoryServiceTests
{
    // Contextually obvious numbers: we add exactly three items, then assert
    // the count is three. The literals are self-documenting and should NOT be
    // flagged as Magic Number Test.
    [Fact]
    public void AddItems_ThreeAdded_CountIsThree()
    {
        var service = new InventoryService();

        service.Add("SKU-1", 1);
        service.Add("SKU-2", 1);
        service.Add("SKU-3", 1);

        Assert.Equal(3, service.ItemCount);
    }

    // Contextually obvious number: removing one of two items leaves one.
    [Fact]
    public void RemoveItem_FromTwo_LeavesOne()
    {
        var service = new InventoryService();
        service.Add("SKU-1", 1);
        service.Add("SKU-2", 1);

        service.Remove("SKU-1");

        Assert.Equal(1, service.ItemCount);
    }

    // Reasoned skip: the annotation documents WHY it is skipped and links a
    // tracking issue. This is less concerning than an unexplained skip.
    [Fact(Skip = "Tracked by #1487 - blocked on the warehouse API redesign")]
    public void Reserve_AcrossWarehouses_BalancesStock()
    {
        var service = new InventoryService();
        service.Add("SKU-1", 10);

        var reserved = service.Reserve("SKU-1", 4);

        Assert.True(reserved);
    }

    // Bare skip: no reason given at all. The reader has no idea why it is
    // disabled or whether the underlying issue is tracked anywhere.
    [Fact(Skip = "skip")]
    public void Restock_FromSupplier_UpdatesQuantities()
    {
        var service = new InventoryService();

        service.Restock("SKU-9", 50);

        Assert.Equal(50, service.QuantityOf("SKU-9"));
    }

    // Sleepy Test: real smell with a clear high-confidence severity rationale.
    [Fact]
    public void Replenish_AsyncJob_Completes()
    {
        var service = new InventoryService();
        service.Add("SKU-1", 1);

        service.ReplenishAsync("SKU-1");
        Thread.Sleep(3000);

        Assert.True(service.WasReplenished("SKU-1"));
    }

    // Assertion-Free Test: real smell, exercises code but verifies nothing.
    [Fact]
    public void Audit_RunsWithoutError()
    {
        var service = new InventoryService();
        service.Add("SKU-1", 1);

        service.Audit();
    }
}
