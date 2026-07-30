using Xunit;

namespace Semantic.Tests;

public interface IAnimal
{
    string Name { get; }
}

public sealed class Dog : IAnimal
{
    public string Name => "Rex";
}

public class SemanticTests
{
    [Fact]
    public void ExactType_ReturnsTypedValue()
    {
        object value = new Dog();

        var dog = Assert.IsType<Dog>(value);

        Assert.Equal("Rex", dog.Name);
    }

    [Fact]
    public void AssignableType_ReturnsTypedValue()
    {
        object value = new Dog();

        var animal = Assert.IsAssignableFrom<IAnimal>(value);

        Assert.Equal("Rex", animal.Name);
    }

    [Fact]
    public void SequenceEquality_ComparesElements()
    {
        IEnumerable<int> expected = [1, 2, 3];
        IEnumerable<int> actual = [1, 2, 3];

        Assert.Equal(expected, actual);
    }
}
