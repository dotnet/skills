using Timing;

namespace Timing.Tests;

public class DurationParserTests
{
    [Fact]
    public void Parse_Seconds_ReturnsSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(45), DurationParser.Parse("45s"));
    }

    [Fact]
    public void Parse_Minutes_ReturnsMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(90), DurationParser.Parse("90m"));
    }

    [Fact]
    public void Parse_Hours_ReturnsHours()
    {
        Assert.Equal(TimeSpan.FromHours(2), DurationParser.Parse("2h"));
    }

    [Fact]
    public void Parse_SurroundingWhitespace_IsIgnored()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), DurationParser.Parse(" 15m "));
    }

    [Fact]
    public void Parse_UnknownUnit_Throws()
    {
        Assert.Throws<FormatException>(() => DurationParser.Parse("5d"));
    }

    [Fact]
    public void Parse_Empty_Throws()
    {
        Assert.Throws<FormatException>(() => DurationParser.Parse(""));
    }

    [Fact]
    public void TryParse_Text_ReturnsFalse()
    {
        Assert.False(DurationParser.TryParse("soon", out _));
    }
}
