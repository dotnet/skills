using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MyApp.Tests;

[TestClass]
public class OrderServiceTests
{
    private static LifecycleProbe fixture = null!;

    [ClassInitialize]
    public static void Initialize(TestContext context) => fixture = new LifecycleProbe();

    [ClassCleanup(ClassCleanupBehavior.EndOfClass)]
    public static void Cleanup() => fixture.Dispose();

    [TestMethod]
    public void CreateOrder_ValidInput_ReturnsOrder()
    {
        fixture.Enter(nameof(CreateOrder_ValidInput_ReturnsOrder));
        var orderId = 42;
        Assert.AreEqual(42, orderId);
        fixture.Exit(nameof(CreateOrder_ValidInput_ReturnsOrder));
    }

    [TestMethod]
    [Timeout(TestTimeout.Infinite)]
    public void ProcessOrder_LargePayload_Completes()
    {
        fixture.Enter(nameof(ProcessOrder_LargePayload_Completes));
        Assert.AreEqual("Order-123", $"Order-{123}");
        fixture.Exit(nameof(ProcessOrder_LargePayload_Completes));
    }

    [TestMethod]
    public void GetOrder_ReturnsExpectedType()
    {
        fixture.Enter(nameof(GetOrder_ReturnsExpectedType));
        object result = "Order-123";
        Assert.IsInstanceOfType(result, typeof(string));
        fixture.Exit(nameof(GetOrder_ReturnsExpectedType));
    }

    [DataTestMethod]
    [DataRow(2, 3, 5, DisplayName = "sum-two-three")]
    [DataRow(-2, 3, 1, DisplayName = "sum-negative")]
    public void AddsRows(int left, int right, int expected)
    {
        var name = left < 0 ? "sum-negative" : "sum-two-three";
        fixture.Enter(name);
        Assert.AreEqual(expected, left + right);
        fixture.Exit(name);
    }

    [TestMethod]
    [Ignore("Requires tracked service TEST-42")]
    public void DeferredService() => Assert.Fail("Must remain skipped.");
}
