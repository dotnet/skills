using NUnit.Framework;

namespace Sample.Tests;

public interface IAnimal;
public sealed class Dog : IAnimal;

[TestFixture]
public class AssertionTests
{
    [Test]
    public void TypeChecksKeepTheirStrictness()
    {
        object value = new Dog();
        Assert.That(value, Is.TypeOf<Dog>());
        Assert.That(value, Is.InstanceOf<IAnimal>());
    }

    [Test]
    public void ExceptionChecksKeepTheirStrictness()
    {
        Assert.Throws<InvalidOperationException>(
            (Action)(() => throw new InvalidOperationException()));
        Assert.Catch<Exception>(
            (Action)(() => throw new InvalidOperationException()));
    }

    [Test]
    public void SequencesCompareByValue()
    {
        int[] expected = [1, 2, 3];
        int[] actual = [1, 2, 3];
        Assert.That(actual, Is.EqualTo(expected));
    }
}
