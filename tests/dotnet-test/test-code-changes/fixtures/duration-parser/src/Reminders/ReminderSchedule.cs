using System.Text.RegularExpressions;
using Timing;

namespace Reminders;

public sealed partial record ReminderSchedule(string Title, TimeSpan Interval)
{
    public static ReminderSchedule Parse(string title, string rule)
    {
        var match = EveryRule().Match(rule.Trim());
        if (!match.Success)
        {
            throw new FormatException($"'{rule}' is not a reminder rule such as 'every 30m'.");
        }

        return new ReminderSchedule(title, DurationParser.Parse(match.Groups["interval"].Value));
    }

    public DateTimeOffset NextAfter(DateTimeOffset previous) => previous + Interval;

    [GeneratedRegex(@"^every\s+(?<interval>\d+[hms])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex EveryRule();
}
