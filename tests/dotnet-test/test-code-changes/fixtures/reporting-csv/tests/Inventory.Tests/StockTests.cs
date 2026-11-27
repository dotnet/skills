using Xunit;

namespace Inventory.Tests;

public sealed class StockTests
{
    [Fact]
    public void Reorders_when_below_minimum() => Assert.True(new Stock(4, 5).NeedsReorder);
}
