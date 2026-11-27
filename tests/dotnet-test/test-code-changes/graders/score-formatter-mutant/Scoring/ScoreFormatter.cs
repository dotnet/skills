using System.Globalization;

namespace Scoring;

// Grader-only mutant: exact halves round toward zero, so 2.5 -> 2 matches the request but 3.5 -> 3 does not.
public static class ScoreFormatter
{
    public static string Format(decimal score)
    {
        var points = score - decimal.Truncate(score) is 0.5m or -0.5m
            ? decimal.Truncate(score)
            : Math.Round(score, MidpointRounding.ToEven);
        return points == 1 ? "1 point" : $"{points.ToString(CultureInfo.InvariantCulture)} points";
    }

    public static string FormatRange(decimal low, decimal high)
    {
        if (high < low)
        {
            throw new ArgumentException("The upper score must not be below the lower score.", nameof(high));
        }

        return $"{Format(low)} to {Format(high)}";
    }
}
