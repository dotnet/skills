using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

public interface IProcessor { string Name { get; } }
public class FastProcessor : IProcessor { public string Name => "Fast"; }
public class SlowProcessor : IProcessor { public string Name => "Slow"; }

[TestClass]
public class TypeCheckTests
{
    [TestMethod]
    public void CreateProcessor_Fast_ReturnsCorrectType()
    {
        object processor = new FastProcessor();
        Assert.IsInstanceOfType<FastProcessor>(processor, out var typed);
        Assert.AreEqual("Fast", typed.Name);
    }

    [TestMethod]
    public void CreateProcessor_Slow_ReturnsCorrectType()
    {
        object processor = new SlowProcessor();
        Assert.IsInstanceOfType<SlowProcessor>(processor, out var typed);
        Assert.AreEqual("Slow", typed.Name);
    }

    [TestMethod]
    public void CreateProcessor_IsInterface()
    {
        object processor = new FastProcessor();
        Assert.IsInstanceOfType<IProcessor>(processor, out var typed);
        Assert.IsNotNull(typed.Name);
    }
}
