using Discounts;
using Xunit;

namespace Discounts.Tests;

public sealed class DiscountRulesTests
{
    private readonly DiscountRules _rules = new();

    [Fact]
    public void Apply_WithNoCode_ReturnsSubtotal()
    {
        Assert.Equal(25m, _rules.Apply(25m, null));
    }

    [Fact]
    public void Apply_WithSave10_ReturnsTenPercentReduction()
    {
        Assert.Equal(90m, _rules.Apply(100m, "SAVE10"));
    }
}
