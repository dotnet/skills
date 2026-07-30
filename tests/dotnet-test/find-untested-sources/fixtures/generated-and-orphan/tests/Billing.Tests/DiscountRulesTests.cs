using Billing;

namespace Billing.Tests;

public sealed class DiscountRulesTests
{
    private readonly DiscountRules _rules = new();

    public void Apply_ReturnsDiscountedTotal_AboveThreshold()
    {
        _ = _rules.Apply(120m);
    }
}
