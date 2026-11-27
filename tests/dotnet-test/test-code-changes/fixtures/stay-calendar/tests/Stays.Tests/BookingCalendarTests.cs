using Stays;

namespace Stays.Tests;

public class BookingCalendarTests
{
    private static DateRange March(int startDay, int endDay) =>
        new(new DateOnly(2025, 3, startDay), new DateOnly(2025, 3, endDay));

    [Fact]
    public void TryBook_EmptyCalendar_AcceptsStay()
    {
        var calendar = new BookingCalendar();

        Assert.True(calendar.TryBook(March(1, 5)));
        Assert.Equal([March(1, 5)], calendar.Stays);
    }

    [Fact]
    public void TryBook_ConflictingStay_IsRejected()
    {
        var calendar = new BookingCalendar();
        calendar.TryBook(March(1, 5));

        Assert.False(calendar.TryBook(March(4, 9)));
        Assert.Single(calendar.Stays);
    }

    [Fact]
    public void IsOccupied_NightInsideStay_IsTrue()
    {
        var calendar = new BookingCalendar();
        calendar.TryBook(March(1, 5));

        Assert.True(calendar.IsOccupied(new DateOnly(2025, 3, 2)));
    }
}
