using Reminders;
using Timing;

var failures = new List<string>();
var total = 0;

void Expect(string text, TimeSpan expected)
{
    total++;
    try
    {
        var parsed = DurationParser.Parse(text);
        var tryParsed = DurationParser.TryParse(text, out var viaTry) ? viaTry : (TimeSpan?)null;
        if (parsed != expected || tryParsed != expected)
        {
            failures.Add($"'{text}': expected {expected}, Parse gave {parsed}, TryParse gave {tryParsed?.ToString() ?? "false"}");
        }
    }
    catch (Exception ex)
    {
        failures.Add($"'{text}' ({ex.GetType().Name})");
    }
}

void Reject(string text)
{
    total++;
    var parseThrows = false;
    try
    {
        DurationParser.Parse(text);
    }
    catch (FormatException)
    {
        parseThrows = true;
    }
    catch (Exception ex)
    {
        failures.Add($"'{text}' threw {ex.GetType().Name} instead of FormatException");
        return;
    }

    if (!parseThrows || DurationParser.TryParse(text, out _))
    {
        failures.Add($"'{text}' should still be rejected");
    }
}

void ExpectReminder(string rule, TimeSpan expected)
{
    total++;
    try
    {
        var interval = ReminderSchedule.Parse("check", rule).Interval;
        if (interval != expected)
        {
            failures.Add($"reminder '{rule}': expected {expected}, got {interval}");
        }
    }
    catch (Exception ex)
    {
        failures.Add($"reminder '{rule}' ({ex.GetType().Name})");
    }
}

void RejectReminder(string rule)
{
    total++;
    try
    {
        ReminderSchedule.Parse("check", rule);
        failures.Add($"reminder '{rule}' should still be rejected");
    }
    catch (FormatException)
    {
    }
    catch (Exception ex)
    {
        failures.Add($"reminder '{rule}' threw {ex.GetType().Name} instead of FormatException");
    }
}

Expect("1h30m", TimeSpan.FromMinutes(90));
Expect("2h5s", new TimeSpan(2, 0, 5));
Expect("1h30m15s", new TimeSpan(1, 30, 15));
Expect("10m30s", new TimeSpan(0, 10, 30));
Expect("45s", TimeSpan.FromSeconds(45));
Expect("90m", TimeSpan.FromMinutes(90));
Expect("2h", TimeSpan.FromHours(2));
Expect(" 15m ", TimeSpan.FromMinutes(15));
Reject("5d");
Reject("");
Reject("soon");
Reject("h");
ExpectReminder("every 1h30m", TimeSpan.FromMinutes(90));
ExpectReminder("every 2h5s", new TimeSpan(2, 0, 5));
ExpectReminder("every 30m", TimeSpan.FromMinutes(30));
ExpectReminder("Every 2h", TimeSpan.FromHours(2));
RejectReminder("daily");
RejectReminder("every soon");

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} of {total} fix checks failed:");
    failures.ForEach(f => Console.Error.WriteLine($"  - {f}"));
    return 1;
}

Console.WriteLine($"fix checks passed: {total}");
return 0;
