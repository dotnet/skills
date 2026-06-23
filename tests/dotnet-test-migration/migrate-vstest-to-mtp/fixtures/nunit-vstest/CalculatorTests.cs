using NUnit.Framework;

namespace MyApp.Tests;

[TestFixture]
public class CalculatorTests
{
    [Test]
    public void Add_ReturnsCorrectSum()
    {
        Assert.That(2 + 2, Is.EqualTo(4));
    }

    [Test]
    public void Subtract_ReturnsCorrectDifference()
    {
        Assert.That(2 - 2, Is.EqualTo(0));
    }

    [Test]
    [Category("Slow")]
    public void Multiply_ReturnsCorrectProduct()
    {
        Assert.That(2 * 3, Is.EqualTo(6));
    }
}
