using System.Globalization;

namespace Scoring;

public static class ScoreFormatter
{
    public static string Format(decimal score)
    {
        var points = Math.Round(score, MidpointRounding.AwayFromZero);
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
