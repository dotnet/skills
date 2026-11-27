using Catalog;

namespace Catalog.Tests;

public class ProductUrlBuilderTests
{
    [Fact]
    public void For_SingleWordNames_UsesLowercaseSegments()
    {
        Assert.Equal("/products/tools/hammer-7", ProductUrlBuilder.For("Tools", "Hammer", 7));
    }

    [Fact]
    public void For_MultiWordNames_JoinsWordsWithDashes()
    {
        Assert.Equal("/products/garden-tools/leaf-rake-12", ProductUrlBuilder.For("Garden Tools", "Leaf Rake", 12));
    }

    [Fact]
    public void For_CategoryWithSymbols_ReplacesEachSymbol()
    {
        Assert.Equal("/products/tools---hardware/claw-hammer-42", ProductUrlBuilder.For("Tools & Hardware", "Claw Hammer", 42));
    }
}
