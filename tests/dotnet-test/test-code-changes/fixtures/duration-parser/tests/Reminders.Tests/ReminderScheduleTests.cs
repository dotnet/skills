using Reminders;

namespace Reminders.Tests;

public class ReminderScheduleTests
{
    [Fact]
    public void Parse_EveryMinutes_SetsInterval()
    {
        var schedule = ReminderSchedule.Parse("Stretch", "every 30m");

        Assert.Equal(new ReminderSchedule("Stretch", TimeSpan.FromMinutes(30)), schedule);
    }

    [Fact]
    public void Parse_RuleKeyword_IgnoresCase()
    {
        Assert.Equal(TimeSpan.FromHours(2), ReminderSchedule.Parse("Backup", "Every 2h").Interval);
    }

    [Fact]
    public void Parse_UnknownRule_Throws()
    {
        Assert.Throws<FormatException>(() => ReminderSchedule.Parse("Standup", "daily"));
    }

    [Fact]
    public void NextAfter_AddsInterval()
    {
        var schedule = new ReminderSchedule("Water", TimeSpan.FromMinutes(45));
        var previous = new DateTimeOffset(2025, 3, 1, 9, 0, 0, TimeSpan.Zero);

        Assert.Equal(previous.AddMinutes(45), schedule.NextAfter(previous));
    }
}
