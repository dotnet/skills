using SkillValidator.Evaluate;

namespace SkillValidator.Tests;

[TestClass]
public class BootstrapConfidenceIntervalTests
{
    [TestMethod]
    public void ReturnsZeroIntervalForEmptyData()
    {
        var ci = Statistics.BootstrapConfidenceInterval([], 0.95);
        Assert.AreEqual(0, ci.Low);
        Assert.AreEqual(0, ci.High);
        Assert.AreEqual(0.95, ci.Level);
    }

    [TestMethod]
    public void ReturnsPointIntervalForSingleDataPoint()
    {
        var ci = Statistics.BootstrapConfidenceInterval([0.5], 0.95);
        Assert.AreEqual(0.5, ci.Low);
        Assert.AreEqual(0.5, ci.High);
    }

    [TestMethod]
    public void ProducesReasonableIntervalForPositiveData()
    {
        double[] data = [0.1, 0.15, 0.2, 0.12, 0.18];
        var ci = Statistics.BootstrapConfidenceInterval(data, 0.95);
        Assert.IsTrue(ci.Low > 0);
        Assert.IsTrue(ci.High > ci.Low);
        Assert.IsTrue(ci.High <= 0.2);
        Assert.AreEqual(0.95, ci.Level);
    }

    [TestMethod]
    public void ProducesIntervalForMixedData()
    {
        double[] data = [-0.1, 0.2, -0.05, 0.15, -0.08, 0.1];
        var ci = Statistics.BootstrapConfidenceInterval(data, 0.95);
        Assert.IsTrue(ci.Low < ci.High);
    }

    [TestMethod]
    public void NarrowerCiWithMoreDataPoints()
    {
        double[] small = [0.1, 0.2, 0.15];
        double[] large = [0.1, 0.2, 0.15, 0.12, 0.18, 0.14, 0.16, 0.13, 0.17, 0.11];
        var ciSmall = Statistics.BootstrapConfidenceInterval(small, 0.95);
        var ciLarge = Statistics.BootstrapConfidenceInterval(large, 0.95);
        var widthSmall = ciSmall.High - ciSmall.Low;
        var widthLarge = ciLarge.High - ciLarge.Low;
        Assert.IsTrue(widthLarge < widthSmall);
    }

    [TestMethod]
    public void IsDeterministic()
    {
        double[] data = [0.1, 0.2, 0.3, 0.15, 0.25];
        var ci1 = Statistics.BootstrapConfidenceInterval(data, 0.95);
        var ci2 = Statistics.BootstrapConfidenceInterval(data, 0.95);
        Assert.AreEqual(ci1.Low, ci2.Low);
        Assert.AreEqual(ci1.High, ci2.High);
    }
}

[TestClass]
public class IsStatisticallySignificantTests
{
    [TestMethod]
    public void ReturnsTrueWhenCiIsEntirelyPositive()
    {
        Assert.IsTrue(Statistics.IsStatisticallySignificant(new ConfidenceInterval(0.05, 0.3, 0.95)));
    }

    [TestMethod]
    public void ReturnsTrueWhenCiIsEntirelyNegative()
    {
        Assert.IsTrue(Statistics.IsStatisticallySignificant(new ConfidenceInterval(-0.4, -0.1, 0.95)));
    }

    [TestMethod]
    public void ReturnsFalseWhenCiSpansZero()
    {
        Assert.IsFalse(Statistics.IsStatisticallySignificant(new ConfidenceInterval(-0.1, 0.2, 0.95)));
    }

    [TestMethod]
    public void ReturnsFalseWhenCiIsExactlyAtZero()
    {
        Assert.IsFalse(Statistics.IsStatisticallySignificant(new ConfidenceInterval(0, 0.1, 0.95)));
    }
}

[TestClass]
public class WilsonScoreIntervalTests
{
    [TestMethod]
    public void ReturnsZeroIntervalForZeroTotal()
    {
        var ci = Statistics.WilsonScoreInterval(0, 0);
        Assert.AreEqual(0, ci.Low);
        Assert.AreEqual(0, ci.High);
    }

    [TestMethod]
    public void ProducesReasonableIntervalForPerfectSuccess()
    {
        var ci = Statistics.WilsonScoreInterval(10, 10);
        Assert.IsTrue(ci.Low > 0.5);
        Assert.AreEqual(1, ci.High);
    }

    [TestMethod]
    public void ProducesReasonableIntervalForNoSuccesses()
    {
        var ci = Statistics.WilsonScoreInterval(0, 10);
        Assert.AreEqual(0, ci.Low);
        Assert.IsTrue(ci.High < 0.5);
    }

    [TestMethod]
    public void ProducesIntervalCenteredAroundProportion()
    {
        var ci = Statistics.WilsonScoreInterval(5, 10);
        Assert.IsTrue(ci.Low < 0.5);
        Assert.IsTrue(ci.High > 0.5);
    }

    [TestMethod]
    public void NarrowsWithMoreSamples()
    {
        var ci10 = Statistics.WilsonScoreInterval(5, 10);
        var ci100 = Statistics.WilsonScoreInterval(50, 100);
        var width10 = ci10.High - ci10.Low;
        var width100 = ci100.High - ci100.Low;
        Assert.IsTrue(width100 < width10);
    }
}

[TestClass]
public class CoefficientOfVariationTests
{
    [TestMethod]
    public void ReturnsNullForEmpty()
    {
        Assert.IsNull(Statistics.CoefficientOfVariation([]));
    }

    [TestMethod]
    public void ReturnsNullForSingleElement()
    {
        Assert.IsNull(Statistics.CoefficientOfVariation([0.5]));
    }

    [TestMethod]
    public void ReturnsNullWhenMeanIsZero()
    {
        Assert.IsNull(Statistics.CoefficientOfVariation([-0.1, 0.1]));
    }

    [TestMethod]
    public void ReturnsZeroForIdenticalValues()
    {
        var cv = Statistics.CoefficientOfVariation([0.3, 0.3, 0.3]);
        Assert.IsNotNull(cv);
        Assert.AreEqual(0.0, cv.Value, 0.001);
    }

    [TestMethod]
    public void ReturnsHighValueForHighVariance()
    {
        // Scores: -0.5, 0.5, -0.3, 0.4 → high spread relative to mean
        var cv = Statistics.CoefficientOfVariation([-0.5, 0.5, -0.3, 0.4]);
        Assert.IsNotNull(cv);
        Assert.IsTrue(cv > 1.0, $"Expected high CV, got {cv}");
    }

    [TestMethod]
    public void ReturnsLowValueForConsistentScores()
    {
        var cv = Statistics.CoefficientOfVariation([0.10, 0.11, 0.09, 0.10, 0.12]);
        Assert.IsNotNull(cv);
        Assert.IsTrue(cv < 0.15, $"Expected low CV, got {cv}");
    }
}
