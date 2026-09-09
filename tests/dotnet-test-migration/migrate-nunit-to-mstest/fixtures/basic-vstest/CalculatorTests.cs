using NUnit.Framework;

namespace Sample.Tests;

[TestFixture]
public class CalculatorTests
{
    [Test]
    public void Add_ReturnsSum()
    {
        Assert.That(2 + 2, Is.EqualTo(4));
    }

    [TestCase(2, 3, 5)]
    [TestCase(-1, 1, 0)]
    public void Add_ReturnsExpected(int left, int right, int expected)
    {
        Assert.That(left + right, Is.EqualTo(expected));
    }
}
