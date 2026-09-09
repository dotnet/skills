using NUnit.Framework;

namespace Sample.Tests;

[TestFixture]
public class InvalidTheoryTests
{
    [Datapoint]
    public int InvalidValue = -1;

    [Theory]
    public void RequiresPositiveValue(int value)
    {
        Assume.That(value > 0);
        Assert.That(value, Is.GreaterThan(0));
    }
}
