using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

public interface IAuditable { }

public interface IShippable { }

public sealed class Order : IAuditable, IShippable
{
    public string Reference { get; init; } = "";
}

[TestClass]
public class ComparisonTests
{
    [TestMethod]
    public void SameOrder_SeenThroughBothInterfaces_IsEqual()
    {
        var order = new Order { Reference = "A-1" };
        IAuditable auditable = order;
        IShippable shippable = order;

        Assert.AreEqual(auditable, shippable);
    }

    [TestMethod]
    public void ReferenceCode_IsNotTheNumericId()
    {
        string referenceCode = "42";
        int numericId = 42;

        Assert.AreNotEqual(referenceCode, numericId);
    }

    [TestMethod]
    public void BothInterfaceViews_AreTheSameInstance()
    {
        var order = new Order { Reference = "A-2" };
        IAuditable auditable = order;
        IShippable shippable = order;

        Assert.AreSame(auditable, shippable);
    }

    // These already compile against MSTest v3 and must be left alone.
    [TestMethod]
    public void TypedAssertions_AreAlreadyValid()
    {
        var order = new Order { Reference = "A-3" };

        Assert.AreEqual("A-3", order.Reference);
        Assert.AreEqual(42, 42);
    }
}
