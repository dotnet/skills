using Stays;

var failures = new List<string>();
var total = 0;

void Check(string behavior, Func<bool> condition)
{
    total++;
    try
    {
        if (!condition())
        {
            failures.Add(behavior);
        }
    }
    catch (Exception ex)
    {
        failures.Add($"{behavior} ({ex.GetType().Name})");
    }
}

static DateRange March(int startDay, int endDay) =>
    new(new DateOnly(2025, 3, startDay), new DateOnly(2025, 3, endDay));

Check("a stay ending on the next stay's check-in day does not overlap it", () => !March(1, 5).Overlaps(March(5, 8)));
Check("touching ranges do not overlap in either order", () => !March(5, 8).Overlaps(March(1, 5)));
Check("ranges sharing one night overlap", () => March(1, 5).Overlaps(March(4, 8)) && March(4, 8).Overlaps(March(1, 5)));
Check("a contained range overlaps", () => March(1, 10).Overlaps(March(3, 4)) && March(3, 4).Overlaps(March(1, 10)));
Check("identical ranges overlap", () => March(2, 6).Overlaps(March(2, 6)));
Check("ranges separated by a gap do not overlap", () => !March(1, 5).Overlaps(March(6, 9)));
Check("back-to-back bookings are accepted", () =>
{
    var calendar = new BookingCalendar();
    return calendar.TryBook(March(1, 5)) && calendar.TryBook(March(5, 8)) && calendar.Stays.Count == 2;
});
Check("conflicting bookings are still rejected", () =>
{
    var calendar = new BookingCalendar();
    return calendar.TryBook(March(1, 5)) && !calendar.TryBook(March(4, 9)) && calendar.Stays.Count == 1;
});
Check("checkout day is still not contained", () => !March(1, 5).Contains(new DateOnly(2025, 3, 5)));
Check("check-in night is still contained", () => March(1, 5).Contains(new DateOnly(2025, 3, 1)));
Check("zero-night stays are still rejected", () =>
{
    try
    {
        _ = March(4, 4);
        return false;
    }
    catch (ArgumentException)
    {
        return true;
    }
});

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} of {total} fix checks failed:");
    failures.ForEach(f => Console.Error.WriteLine($"  - {f}"));
    return 1;
}

Console.WriteLine($"fix checks passed: {total}");
return 0;
