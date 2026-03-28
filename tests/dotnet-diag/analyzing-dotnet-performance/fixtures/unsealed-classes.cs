namespace Textualizer.Localisation;

/// <summary>
/// Defines the contract for formatting date/time humanization output
/// in a locale-specific manner.
/// </summary>
public interface IFormatter
{
    /// <summary>Formats a "just now" reference.</summary>
    string DateHumanize_Now();

    /// <summary>Formats a "never" reference.</summary>
    string DateHumanize_Never();

    /// <summary>Formats a time unit reference (e.g., "2 hours ago").</summary>
    string TimeUnitHumanize(TimeUnit timeUnit, int count, bool toWords = false);
}

/// <summary>
/// Defines the contract for ordinalizing numbers in a locale-specific manner.
/// </summary>
public interface IOrdinalizer
{
    /// <summary>
    /// Converts a number to its ordinal string representation (e.g., 1 → "1st").
    /// </summary>
    string Convert(int number, string numberString);
}

/// <summary>
/// Defines the contract for providing date-to-ordinal-words conversion.
/// </summary>
public interface IDateToOrdinalWordsConverter
{
    /// <summary>Converts a date to its ordinal words representation.</summary>
    string Convert(DateTime date);
}

// --- Unsealed classes (the anti-pattern) ---
// In Humanizer, ~185 of 186 non-abstract, non-static classes are unsealed.
// JIT cannot devirtualize virtual/interface calls or optimize type checks
// without the sealed keyword.

/// <summary>
/// Default implementation of <see cref="IFormatter"/> providing English date/time formatting.
/// </summary>
// Anti-pattern: not sealed — this is a leaf class (not subclassed by the classes below,
// which each have their own base). JIT cannot devirtualize calls.
public class DefaultFormatter : IFormatter
{
    /// <inheritdoc />
    public virtual string DateHumanize_Now() => "now";

    /// <inheritdoc />
    public virtual string DateHumanize_Never() => "never";

    /// <inheritdoc />
    public virtual string TimeUnitHumanize(TimeUnit timeUnit, int count, bool toWords = false)
    {
        var unit = timeUnit.ToString().ToLowerInvariant();
        if (count != 1)
        {
            unit += "s";
        }

        return toWords ? $"{NumberToWords(count)} {unit}" : $"{count} {unit}";
    }

    private static string NumberToWords(int number) =>
        number switch
        {
            1 => "one",
            2 => "two",
            3 => "three",
            _ => number.ToString()
        };
}

/// <summary>
/// German-specific formatter providing localized date/time humanization.
/// </summary>
// Anti-pattern: not sealed — leaf class
public class GermanFormatter : IFormatter
{
    /// <inheritdoc />
    public virtual string DateHumanize_Now() => "jetzt";

    /// <inheritdoc />
    public virtual string DateHumanize_Never() => "nie";

    /// <inheritdoc />
    public virtual string TimeUnitHumanize(TimeUnit timeUnit, int count, bool toWords = false)
    {
        var unit = GetGermanUnit(timeUnit, count);
        return $"{count} {unit}";
    }

    private static string GetGermanUnit(TimeUnit timeUnit, int count) =>
        (timeUnit, count) switch
        {
            (TimeUnit.Year, 1) => "Jahr",
            (TimeUnit.Year, _) => "Jahre",
            (TimeUnit.Month, 1) => "Monat",
            (TimeUnit.Month, _) => "Monate",
            (TimeUnit.Day, 1) => "Tag",
            (TimeUnit.Day, _) => "Tage",
            (TimeUnit.Hour, 1) => "Stunde",
            (TimeUnit.Hour, _) => "Stunden",
            (TimeUnit.Minute, 1) => "Minute",
            (TimeUnit.Minute, _) => "Minuten",
            (TimeUnit.Second, 1) => "Sekunde",
            (TimeUnit.Second, _) => "Sekunden",
            _ => timeUnit.ToString()
        };
}

/// <summary>
/// French-specific formatter providing localized date/time humanization.
/// </summary>
// Anti-pattern: not sealed — leaf class
public class FrenchFormatter : IFormatter
{
    /// <inheritdoc />
    public virtual string DateHumanize_Now() => "maintenant";

    /// <inheritdoc />
    public virtual string DateHumanize_Never() => "jamais";

    /// <inheritdoc />
    public virtual string TimeUnitHumanize(TimeUnit timeUnit, int count, bool toWords = false)
    {
        var unit = GetFrenchUnit(timeUnit, count);
        return $"{count} {unit}";
    }

    private static string GetFrenchUnit(TimeUnit timeUnit, int count) =>
        (timeUnit, count) switch
        {
            (TimeUnit.Year, 1) => "an",
            (TimeUnit.Year, _) => "ans",
            (TimeUnit.Month, _) => "mois",
            (TimeUnit.Day, 1) => "jour",
            (TimeUnit.Day, _) => "jours",
            (TimeUnit.Hour, 1) => "heure",
            (TimeUnit.Hour, _) => "heures",
            (TimeUnit.Minute, 1) => "minute",
            (TimeUnit.Minute, _) => "minutes",
            (TimeUnit.Second, 1) => "seconde",
            (TimeUnit.Second, _) => "secondes",
            _ => timeUnit.ToString()
        };
}

/// <summary>
/// Default ordinalizer providing English ordinal suffixes (1st, 2nd, 3rd, etc.).
/// </summary>
// Anti-pattern: not sealed — leaf class (base for locale ordinalizers below)
// Actually, this IS a base class — keep it unsealed. But its derived classes should be sealed.
public class DefaultOrdinalizer : IOrdinalizer
{
    /// <inheritdoc />
    public virtual string Convert(int number, string numberString)
    {
        var suffix = (number % 100) switch
        {
            11 or 12 or 13 => "th",
            _ => (number % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            }
        };

        return numberString + suffix;
    }
}

/// <summary>
/// Spanish ordinalizer providing locale-specific ordinal formatting.
/// </summary>
// Anti-pattern: not sealed — derived leaf class, should be sealed
public class SpanishOrdinalizer : DefaultOrdinalizer
{
    /// <inheritdoc />
    public override string Convert(int number, string numberString) =>
        numberString + ".º";
}

/// <summary>
/// Italian ordinalizer providing locale-specific ordinal formatting.
/// </summary>
// Anti-pattern: not sealed — derived leaf class, should be sealed
public class ItalianOrdinalizer : DefaultOrdinalizer
{
    /// <inheritdoc />
    public override string Convert(int number, string numberString) =>
        numberString + "°";
}

/// <summary>
/// Romanian ordinalizer providing locale-specific ordinal formatting.
/// </summary>
// Anti-pattern: not sealed — derived leaf class
public class RomanianOrdinalizer : DefaultOrdinalizer
{
    /// <inheritdoc />
    public override string Convert(int number, string numberString)
    {
        // Complex Romanian ordinal logic
        if (number < 0)
        {
            return numberString;
        }

        return (number % 100) switch
        {
            1 => "primul",
            2 => "al doilea",
            _ => $"al {numberString}-lea"
        };
    }
}

/// <summary>
/// Default date-to-ordinal-words converter.
/// </summary>
// Anti-pattern: not sealed — leaf class
public class DefaultDateToOrdinalWordsConverter : IDateToOrdinalWordsConverter
{
    /// <inheritdoc />
    public virtual string Convert(DateTime date) =>
        $"{date:MMMM} {date.Day}, {date.Year}";
}

/// <summary>
/// Registry for locale-specific formatters, ordinalizers, and converters.
/// Provides thread-safe registration and lookup of locale implementations.
/// </summary>
// Anti-pattern: not sealed — leaf class, no subclasses
public class LocaleRegistry
{
    private readonly Dictionary<string, IFormatter> _formatters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IOrdinalizer> _ordinalizers = new(StringComparer.OrdinalIgnoreCase);
    private readonly IFormatter _defaultFormatter = new DefaultFormatter();
    private readonly IOrdinalizer _defaultOrdinalizer = new DefaultOrdinalizer();

    /// <summary>
    /// Registers a formatter for the specified culture.
    /// </summary>
    /// <param name="culture">The culture identifier (e.g., "en", "de", "fr").</param>
    /// <param name="formatter">The formatter to register.</param>
    public void RegisterFormatter(string culture, IFormatter formatter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        ArgumentNullException.ThrowIfNull(formatter);
        _formatters[culture] = formatter;
    }

    /// <summary>
    /// Registers an ordinalizer for the specified culture.
    /// </summary>
    /// <param name="culture">The culture identifier.</param>
    /// <param name="ordinalizer">The ordinalizer to register.</param>
    public void RegisterOrdinalizer(string culture, IOrdinalizer ordinalizer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        ArgumentNullException.ThrowIfNull(ordinalizer);
        _ordinalizers[culture] = ordinalizer;
    }

    /// <summary>
    /// Gets the formatter for the specified culture, falling back to the default.
    /// </summary>
    /// <param name="culture">The culture identifier.</param>
    /// <returns>The locale-specific or default formatter.</returns>
    public IFormatter GetFormatter(string culture) =>
        _formatters.TryGetValue(culture, out var formatter) ? formatter : _defaultFormatter;

    /// <summary>
    /// Gets the ordinalizer for the specified culture, falling back to the default.
    /// </summary>
    /// <param name="culture">The culture identifier.</param>
    /// <returns>The locale-specific or default ordinalizer.</returns>
    public IOrdinalizer GetOrdinalizer(string culture) =>
        _ordinalizers.TryGetValue(culture, out var ordinalizer) ? ordinalizer : _defaultOrdinalizer;
}

/// <summary>
/// Strategies for humanizing date/time differences.
/// </summary>
// Anti-pattern: not sealed — leaf class
public class DefaultDateTimeHumanizeStrategy
{
    /// <summary>
    /// Humanizes a DateTime difference relative to a base date.
    /// </summary>
    /// <param name="input">The date to humanize.</param>
    /// <param name="comparedTo">The reference date (typically DateTime.UtcNow).</param>
    /// <returns>A human-readable string representing the time difference.</returns>
    public virtual string Humanize(DateTime input, DateTime comparedTo)
    {
        var difference = comparedTo - input;
        if (Math.Abs(difference.TotalSeconds) < 2)
        {
            return "just now";
        }

        var isFuture = difference.TotalSeconds < 0;
        var absDiff = difference.Duration();

        var result = absDiff.TotalDays switch
        {
            >= 365 => $"{(int)(absDiff.TotalDays / 365)} years",
            >= 31 => $"{(int)(absDiff.TotalDays / 30)} months",
            >= 1 => $"{(int)absDiff.TotalDays} days",
            _ => absDiff.TotalHours switch
            {
                >= 1 => $"{(int)absDiff.TotalHours} hours",
                _ => absDiff.TotalMinutes switch
                {
                    >= 1 => $"{(int)absDiff.TotalMinutes} minutes",
                    _ => $"{(int)absDiff.TotalSeconds} seconds"
                }
            }
        };

        return isFuture ? $"in {result}" : $"{result} ago";
    }
}

/// <summary>
/// Provides formatting for data unit names used in ByteSize humanization.
/// </summary>
// Anti-pattern: not sealed — leaf class
public class DataUnitFormatter
{
    /// <summary>
    /// Gets the display name for a data unit, optionally as a symbol.
    /// </summary>
    /// <param name="unit">The data unit.</param>
    /// <param name="toSymbol">Whether to return the symbol instead of the full name.</param>
    /// <returns>The display name or symbol for the unit.</returns>
    public virtual string Format(string unit, bool toSymbol) =>
        toSymbol ? GetSymbol(unit) : unit;

    private static string GetSymbol(string unit) =>
        unit switch
        {
            "Byte" => "B",
            "Kilobyte" => "KB",
            "Megabyte" => "MB",
            "Gigabyte" => "GB",
            "Terabyte" => "TB",
            "Bit" => "b",
            _ => unit
        };
}
