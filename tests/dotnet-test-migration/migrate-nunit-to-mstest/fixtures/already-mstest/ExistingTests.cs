using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.VisualStudio.TestTools.UnitTesting.Combinatorial;

namespace Sample.Tests;

[TestClass]
public class ExistingTests
{
    private readonly TestContext _testContext;

    public ExistingTests(TestContext testContext)
    {
        _testContext = testContext;
    }

    public static IEnumerable<object[]> TheoryRows
    {
        get
        {
            foreach (int value in new[] { -1, 0, 2 })
            {
                yield return [value, false];
                yield return [value, true];
            }
        }
    }

    [TestMethod]
    public void ExistingTestPasses()
    {
        Assert.AreEqual(4, Add(2, 2));
    }

    [TestMethod]
    [CombinatorialData]
    public void ExistingCombinatorialTestPasses(
        [CombinatorialValues(1, 2)] int left,
        [CombinatorialRange(1, 2, 1)] int right)
    {
        Assert.IsGreaterThan(0, left + right);
    }

    [TestMethod]
    [Timeout(5_000, CooperativeCancellation = true)]
    public async Task ExistingCooperativeTimeoutUsesTestContext()
    {
        await Task.Delay(1, _testContext.CancellationToken);
        Assert.IsTrue(_testContext.CancellationToken.CanBeCanceled);
    }

    [TestMethod]
    [DynamicData(nameof(TheoryRows))]
    public void ExistingTheoryRowsRemainDiscoverable(int value, bool requirePositive)
    {
        if (requirePositive && value <= 0)
        {
            Assert.Inconclusive("The theory assumption is not satisfied.");
        }

        Assert.IsGreaterThanOrEqualTo(0, value * value);
    }

    private static int Add(int left, int right) => left + right;
}
