using Stays;

namespace Stays.Tests;

public class DateRangeTests
{
    private static DateRange March(int startDay, int endDay) =>
        new(new DateOnly(2025, 3, startDay), new DateOnly(2025, 3, endDay));

    [Fact]
    public void Constructor_EndNotAfterStart_Throws()
    {
        var day = new DateOnly(2025, 3, 4);

        Assert.Throws<ArgumentException>(() => new DateRange(day, day));
    }

    [Fact]
    public void Nights_CountsCheckInToCheckOut()
    {
        Assert.Equal(4, March(1, 5).Nights);
    }

    [Fact]
    public void Contains_CheckInNight_IsTrue()
    {
        Assert.True(March(1, 5).Contains(new DateOnly(2025, 3, 1)));
    }

    [Fact]
    public void Contains_CheckOutDay_IsFalse()
    {
        Assert.False(March(1, 5).Contains(new DateOnly(2025, 3, 5)));
    }

    [Fact]
    public void Overlaps_SharedNights_IsTrue()
    {
        Assert.True(March(1, 5).Overlaps(March(3, 8)));
    }

    [Fact]
    public void Overlaps_SeparatedByAGap_IsFalse()
    {
        Assert.False(March(1, 5).Overlaps(March(10, 12)));
    }
}
