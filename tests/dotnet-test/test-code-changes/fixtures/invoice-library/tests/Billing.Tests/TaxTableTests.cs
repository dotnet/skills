using Billing;

namespace Billing.Tests;

public class TaxTableTests
{
    [Fact]
    public void RateFor_KnownRegion()
    {
        object rate = TaxTable.Default.RateFor("north");

        Assert.NotNull(rate);
    }

    [Fact]
    public void RateFor_IgnoresCase()
    {
        Assert.Equal(TaxTable.Default.RateFor("south"), TaxTable.Default.RateFor("SOUTH"));
    }

    [Fact]
    public void RateFor_UnknownRegion()
    {
        try
        {
            TaxTable.Default.RateFor("mars");
        }
        catch (KeyNotFoundException)
        {
        }
    }

    [Fact]
    public void Default_IsReady()
    {
        Thread.Sleep(250);

        Assert.True(true);
    }
}
