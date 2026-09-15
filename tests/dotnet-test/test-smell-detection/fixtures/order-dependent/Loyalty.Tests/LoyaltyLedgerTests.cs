using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Contoso.Loyalty.Tests;

[TestClass]
public class LoyaltyLedgerTests
{
    private const int GoldThreshold = 500;

    private static readonly string[] TierNames = { "Bronze", "Silver", "Gold" };

    private static readonly List<int> AwardedPoints = new();

    private static int _tierIndex;

    [TestMethod]
    public void AwardPoints_RecordsTheFirstPurchase()
    {
        AwardedPoints.Add(200);

        Assert.AreEqual(1, AwardedPoints.Count);
    }

    [TestMethod]
    public void AwardPoints_AccumulatesAcrossPurchases()
    {
        AwardedPoints.Add(350);

        Assert.AreEqual(550, Total(AwardedPoints));
    }

    [TestMethod]
    public void PromoteMember_ReachesGoldAtThreshold()
    {
        if (Total(AwardedPoints) >= GoldThreshold)
        {
            _tierIndex = 2;
        }

        Assert.AreEqual("Gold", TierNames[_tierIndex]);
    }

    [TestMethod]
    public void ResetLedger_StartsANewProgramYear()
    {
        AwardedPoints.Clear();
        _tierIndex = 0;

        Assert.AreEqual(0, AwardedPoints.Count);
        Assert.AreEqual("Bronze", TierNames[_tierIndex]);
    }

    private static int Total(List<int> points)
    {
        var sum = 0;
        foreach (var value in points)
        {
            sum += value;
        }

        return sum;
    }
}
