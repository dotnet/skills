using NUnit.Framework;

namespace Sample.Tests;

[TestFixture]
public class TheoryTests
{
    [Datapoint]
    public int Negative = -1;

    [DatapointSource]
    public static readonly int[] NonNegativeValues = [0, 2];

    [Theory]
    public void SquareHasExpectedSign(int value, bool requirePositive)
    {
        Assume.That(!requirePositive || value > 0);
        Assert.That(value * value, Is.GreaterThanOrEqualTo(0));
    }
}
