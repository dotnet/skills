using NUnit.Framework;

namespace Sample.Tests;

[TestFixture]
public class CombinationTests
{
    [Test]
    [Combinatorial]
    public void Sum_IsPositive(
        [Values(1, 2)] int left,
        [Range(1, 2)] int right)
    {
        Assert.That(left + right, Is.GreaterThan(0));
    }
}
