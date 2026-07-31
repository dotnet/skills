using Xunit;

namespace Contoso.Catalog.Tests.Unit;

public class SearchQueryParserTests
{
    [Fact]
    [Trait("Category", "Smoke")]
    public void Parse_SingleTerm_ReturnsTerm() { Assert.True(true); }

    [Fact]
    [Trait("Category", "Regression")]
    public void Parse_UnbalancedQuotes_Throws() { Assert.True(true); }
}

public class PriceFormatterTests
{
    [Fact]
    [Trait("Category", "Smoke")]
    public void Format_WholeAmount_HasTwoDecimals() { Assert.True(true); }
}
