using NUnit.Framework;

namespace Sample.Tests;

[TestFixture]
public class CalculatorTests
{
    private int _offset;

    [SetUp]
    public void SetUp()
    {
        _offset = 1;
    }

    [TearDown]
    public void TearDown()
    {
        _offset = 0;
    }

    [Test]
    public void Add_ReturnsSum()
    {
        Assert.That(2 + 2 + _offset, Is.EqualTo(5));
    }

    [TestCase(2, 3, 6)]
    [TestCase(-1, 1, 1)]
    public void Add_ReturnsExpected(int left, int right, int expected)
    {
        Assert.That(left + right + _offset, Is.EqualTo(expected));
    }
}
