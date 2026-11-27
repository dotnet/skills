using Scoring;

var failures = new List<string>();
var total = 0;

void Expect(string behavior, Func<string> actual, string expected)
{
    total++;
    try
    {
        var value = actual();
        if (value != expected)
        {
            failures.Add($"{behavior}: expected '{expected}', got '{value}'");
        }
    }
    catch (Exception ex)
    {
        failures.Add($"{behavior} ({ex.GetType().Name})");
    }
}

Expect("2.5 rounds to the even 2", () => ScoreFormatter.Format(2.5m), "2 points");
Expect("3.5 rounds to the even 4", () => ScoreFormatter.Format(3.5m), "4 points");
Expect("0.5 rounds to the even 0", () => ScoreFormatter.Format(0.5m), "0 points");
Expect("1.5 rounds to the even 2", () => ScoreFormatter.Format(1.5m), "2 points");
Expect("-2.5 rounds to the even -2", () => ScoreFormatter.Format(-2.5m), "-2 points");
Expect("values below half still round down", () => ScoreFormatter.Format(2.4m), "2 points");
Expect("values above half still round up", () => ScoreFormatter.Format(2.6m), "3 points");
Expect("whole numbers are unchanged", () => ScoreFormatter.Format(7m), "7 points");
Expect("one point stays singular", () => ScoreFormatter.Format(1m), "1 point");
Expect("ranges use the new rounding at both ends", () => ScoreFormatter.FormatRange(2.5m, 3.5m), "2 points to 4 points");
Expect("ranges still join both ends", () => ScoreFormatter.FormatRange(1.2m, 4.7m), "1 point to 5 points");
Expect("an inverted range still throws", () =>
{
    try
    {
        return ScoreFormatter.FormatRange(4m, 2m);
    }
    catch (ArgumentException)
    {
        return "throws";
    }
}, "throws");

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} of {total} fix checks failed:");
    failures.ForEach(f => Console.Error.WriteLine($"  - {f}"));
    return 1;
}

Console.WriteLine($"fix checks passed: {total}");
return 0;
