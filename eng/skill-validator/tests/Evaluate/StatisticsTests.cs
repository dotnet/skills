using SkillValidator.Evaluate;

namespace SkillValidator.Tests;

public class BootstrapConfidenceIntervalTests
{
    [Fact]
    public void ReturnsZeroIntervalForEmptyData()
    {
        var ci = Statistics.BootstrapConfidenceInterval([], 0.95);
        Assert.Equal(0, ci.Low);
        Assert.Equal(0, ci.High);
        Assert.Equal(0.95, ci.Level);
    }

    [Fact]
    public void ReturnsPointIntervalForSingleDataPoint()
    {
        var ci = Statistics.BootstrapConfidenceInterval([0.5], 0.95);
        Assert.Equal(0.5, ci.Low);
        Assert.Equal(0.5, ci.High);
    }

    [Fact]
    public void ProducesReasonableIntervalForPositiveData()
    {
        double[] data = [0.1, 0.15, 0.2, 0.12, 0.18];
        var ci = Statistics.BootstrapConfidenceInterval(data, 0.95);
        Assert.True(ci.Low > 0);
        Assert.True(ci.High > ci.Low);
        Assert.True(ci.High <= 0.2);
        Assert.Equal(0.95, ci.Level);
    }

    [Fact]
    public void ProducesIntervalForMixedData()
    {
        double[] data = [-0.1, 0.2, -0.05, 0.15, -0.08, 0.1];
        var ci = Statistics.BootstrapConfidenceInterval(data, 0.95);
        Assert.True(ci.Low < ci.High);
    }

    [Fact]
    public void NarrowerCiWithMoreDataPoints()
    {
        double[] small = [0.1, 0.2, 0.15];
        double[] large = [0.1, 0.2, 0.15, 0.12, 0.18, 0.14, 0.16, 0.13, 0.17, 0.11];
        var ciSmall = Statistics.BootstrapConfidenceInterval(small, 0.95);
        var ciLarge = Statistics.BootstrapConfidenceInterval(large, 0.95);
        var widthSmall = ciSmall.High - ciSmall.Low;
        var widthLarge = ciLarge.High - ciLarge.Low;
        Assert.True(widthLarge < widthSmall);
    }

    [Fact]
    public void IsDeterministic()
    {
        double[] data = [0.1, 0.2, 0.3, 0.15, 0.25];
        var ci1 = Statistics.BootstrapConfidenceInterval(data, 0.95);
        var ci2 = Statistics.BootstrapConfidenceInterval(data, 0.95);
        Assert.Equal(ci1.Low, ci2.Low);
        Assert.Equal(ci1.High, ci2.High);
    }
}

public class IsStatisticallySignificantTests
{
    [Fact]
    public void ReturnsTrueWhenCiIsEntirelyPositive()
    {
        Assert.True(Statistics.IsStatisticallySignificant(new ConfidenceInterval(0.05, 0.3, 0.95)));
    }

    [Fact]
    public void ReturnsTrueWhenCiIsEntirelyNegative()
    {
        Assert.True(Statistics.IsStatisticallySignificant(new ConfidenceInterval(-0.4, -0.1, 0.95)));
    }

    [Fact]
    public void ReturnsFalseWhenCiSpansZero()
    {
        Assert.False(Statistics.IsStatisticallySignificant(new ConfidenceInterval(-0.1, 0.2, 0.95)));
    }

    [Fact]
    public void ReturnsFalseWhenCiIsExactlyAtZero()
    {
        Assert.False(Statistics.IsStatisticallySignificant(new ConfidenceInterval(0, 0.1, 0.95)));
    }
}

public class WilsonScoreIntervalTests
{
    [Fact]
    public void ReturnsZeroIntervalForZeroTotal()
    {
        var ci = Statistics.WilsonScoreInterval(0, 0);
        Assert.Equal(0, ci.Low);
        Assert.Equal(0, ci.High);
    }

    [Fact]
    public void ProducesReasonableIntervalForPerfectSuccess()
    {
        var ci = Statistics.WilsonScoreInterval(10, 10);
        Assert.True(ci.Low > 0.5);
        Assert.Equal(1, ci.High);
    }

    [Fact]
    public void ProducesReasonableIntervalForNoSuccesses()
    {
        var ci = Statistics.WilsonScoreInterval(0, 10);
        Assert.Equal(0, ci.Low);
        Assert.True(ci.High < 0.5);
    }

    [Fact]
    public void ProducesIntervalCenteredAroundProportion()
    {
        var ci = Statistics.WilsonScoreInterval(5, 10);
        Assert.True(ci.Low < 0.5);
        Assert.True(ci.High > 0.5);
    }

    [Fact]
    public void NarrowsWithMoreSamples()
    {
        var ci10 = Statistics.WilsonScoreInterval(5, 10);
        var ci100 = Statistics.WilsonScoreInterval(50, 100);
        var width10 = ci10.High - ci10.Low;
        var width100 = ci100.High - ci100.Low;
        Assert.True(width100 < width10);
    }
}
