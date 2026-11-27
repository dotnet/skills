using Scoring;

namespace Scoring.Tests;

public class ScoreFormatterTests
{
    [Fact]
    public void Format_WholeNumber_IsUnchanged()
    {
        Assert.Equal("7 points", ScoreFormatter.Format(7m));
    }

    [Fact]
    public void Format_BelowHalf_RoundsDown()
    {
        Assert.Equal("2 points", ScoreFormatter.Format(2.4m));
    }

    [Fact]
    public void Format_AboveHalf_RoundsUp()
    {
        Assert.Equal("3 points", ScoreFormatter.Format(2.6m));
    }

    [Fact]
    public void Format_ExactlyHalf_RoundsUp()
    {
        Assert.Equal("3 points", ScoreFormatter.Format(2.5m));
    }

    [Fact]
    public void Format_One_IsSingular()
    {
        Assert.Equal("1 point", ScoreFormatter.Format(1m));
    }

    [Fact]
    public void FormatRange_JoinsBothEnds()
    {
        Assert.Equal("1 point to 5 points", ScoreFormatter.FormatRange(1.2m, 4.7m));
    }

    [Fact]
    public void FormatRange_HighBelowLow_Throws()
    {
        Assert.Throws<ArgumentException>(() => ScoreFormatter.FormatRange(4m, 2m));
    }
}
