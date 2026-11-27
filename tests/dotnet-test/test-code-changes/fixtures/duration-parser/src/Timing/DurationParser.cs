using System.Globalization;
using System.Text.RegularExpressions;

namespace Timing;

public static partial class DurationParser
{
    public static TimeSpan Parse(string text) =>
        TryParse(text, out var duration)
            ? duration
            : throw new FormatException($"'{text}' is not a duration such as 45s, 90m or 2h.");

    public static bool TryParse(string? text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var match = Component().Match(text?.Trim() ?? "");
        if (!match.Success)
        {
            return false;
        }

        var value = int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
        duration = match.Groups["unit"].Value switch
        {
            "h" => TimeSpan.FromHours(value),
            "m" => TimeSpan.FromMinutes(value),
            _ => TimeSpan.FromSeconds(value),
        };
        return true;
    }

    [GeneratedRegex(@"^(?<value>\d+)(?<unit>[hms])", RegexOptions.CultureInvariant)]
    private static partial Regex Component();
}
