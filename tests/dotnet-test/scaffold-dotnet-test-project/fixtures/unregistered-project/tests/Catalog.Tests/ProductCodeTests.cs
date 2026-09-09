using Catalog;
using Xunit;

namespace Catalog.Tests;

public sealed class ProductCodeTests
{
    [Fact]
    public void Constructor_PreservesValue()
    {
        Assert.Equal("SKU-42", new ProductCode("SKU-42").Value);
    }
}
