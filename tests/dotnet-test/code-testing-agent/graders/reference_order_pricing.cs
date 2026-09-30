using Orders;
using Xunit;

public class PricingExamples
{
    [Fact]
    public void InvalidInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OrderPricing.Total(-0.01m, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => OrderPricing.Total(1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => OrderPricing.Total(1, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => OrderPricing.Total(1, 1, -0.01m));
        Assert.Throws<ArgumentOutOfRangeException>(() => OrderPricing.Total(1, 1, 100.01m));
    }

    [Fact]
    public void ExactAmounts()
    {
        Assert.Equal(0m, OrderPricing.Total(0m, 3, 12.5m));
        Assert.Equal(37.02m, OrderPricing.Total(12.34m, 3, 0));
        Assert.Equal(0m, OrderPricing.Total(12.34m, 3, 100));
        Assert.Equal(32.3925m, OrderPricing.Total(12.34m, 3, 12.5m));
    }
}
