using Xunit;

namespace Scheduling.Tests;

public sealed class ShiftTests
{
    [Fact]
    public void Length_is_the_difference_in_hours() => Assert.Equal(8, new Shift(9, 17).Length);
}
