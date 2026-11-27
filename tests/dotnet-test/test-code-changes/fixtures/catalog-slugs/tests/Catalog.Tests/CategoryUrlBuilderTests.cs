using Catalog;

namespace Catalog.Tests;

public class CategoryUrlBuilderTests
{
    [Fact]
    public void For_SimpleNames_BuildsTwoSegments()
    {
        Assert.Equal("/c/home/kitchen", CategoryUrlBuilder.For("Home", "Kitchen"));
    }

    [Fact]
    public void For_PaddedName_TrimsOuterDashes()
    {
        Assert.Equal("/c/outdoor/patio", CategoryUrlBuilder.For("Outdoor", " Patio! "));
    }

    [Fact]
    public void For_DigitsInName_AreKept()
    {
        Assert.Equal("/c/toys/age-3-plus", CategoryUrlBuilder.For("Toys", "Age 3 Plus"));
    }
}
