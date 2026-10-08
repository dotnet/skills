using Orders;
using Xunit;

public class WindowExamples
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidDuration(long ticks)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReservationWindow(TimeSpan.FromTicks(ticks)));
    }

    [Fact]
    public void ExactInstants()
    {
        var start = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));
        var duration = TimeSpan.FromMinutes(5);
        var window = new ReservationWindow(duration);
        Assert.Equal(duration, window.HoldDuration);
        Assert.False(window.IsActive(start, start.AddTicks(-1)));
        Assert.True(window.IsActive(start, start));
        Assert.True(window.IsActive(start, start + duration - TimeSpan.FromTicks(1)));
        Assert.False(window.IsActive(start, start + duration));
    }
}
