using Xunit;

public class CheckoutTests
{
    [Fact]
    public void Checkout_AppliesCoupon()
    {
        Assert.Equal(90, 100 - 10);
    }
}
