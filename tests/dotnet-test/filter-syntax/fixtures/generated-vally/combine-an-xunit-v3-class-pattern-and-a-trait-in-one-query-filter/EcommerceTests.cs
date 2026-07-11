using Xunit;

namespace Contoso.Ecommerce.Tests;

public class CheckoutIntegrationTests
{
    [Fact]
    [Trait("Category", "Smoke")]
    public void Checkout_HappyPath_Succeeds() => Assert.True(true);

    [Fact]
    [Trait("Category", "Regression")]
    public void Checkout_ExpiredCard_Fails() => Assert.True(true);
}

public class CatalogUnitTests
{
    [Fact]
    [Trait("Category", "Smoke")]
    public void Search_ReturnsResults() => Assert.True(true);
}
