using System.Globalization;

namespace Textualizer;

/// <summary>
/// Specifies the unit of time for humanization purposes.
/// Values are ordered from largest to smallest.
/// </summary>
public enum TimeUnit
{
    /// <summary>Years.</summary>
    Year,
    /// <summary>Months.</summary>
    Month,
    /// <summary>Weeks.</summary>
    Week,
    /// <summary>Days.</summary>
    Day,
    /// <summary>Hours.</summary>
    Hour,
    /// <summary>Minutes.</summary>
    Minute,
    /// <summary>Seconds.</summary>
    Second,
    /// <summary>Milliseconds.</summary>
    Millisecond
}

/// <summary>
/// Provides extension methods for converting <see cref="TimeSpan"/> values to
/// human-readable strings with configurable precision, culture, and unit ranges.
/// </summary>
public static class TimeSpanReadabilityExtensions
{
    private const int DaysInAWeek = 7;
    private const double DaysInAYear = 365.2425;
    private const double DaysInAMonth = DaysInAYear / 12;

    // Anti-pattern: Enumerable.Reverse at static init — allocates an intermediate array
    // Could be replaced with a reversed array literal: [TimeUnit.Millisecond, ..., TimeUnit.Year]
    private static readonly TimeUnit[] TimeUnits = [.. Enumerable.Reverse(Enum.GetValues<TimeUnit>())];

    /// <summary>
    /// Converts a <see cref="TimeSpan"/> to a human-readable string.
    /// </summary>
    /// <param name="timeSpan">The time span to humanize.</param>
    /// <param name="precision">The number of time units to include (e.g., 2 for "1 hour, 30 minutes").</param>
    /// <param name="culture">The culture for localized formatting. Uses current culture if null.</param>
    /// <param name="maxUnit">The largest time unit to display.</param>
    /// <param name="minUnit">The smallest time unit to display.</param>
    /// <param name="collectionSeparator">The separator between time parts (default: ", ").</param>
    /// <param name="countEmptyUnits">Whether empty units count toward precision.</param>
    /// <returns>A human-readable string representation of the time span.</returns>
    /// <example>
    /// <code>
    /// TimeSpan.FromMinutes(90).Humanize() => "1 hour"
    /// TimeSpan.FromMinutes(90).Humanize(precision: 2) => "1 hour, 30 minutes"
    /// TimeSpan.FromDays(1.5).Humanize(precision: 3) => "1 day, 12 hours"
    /// </code>
    /// </example>
    public static string Humanize(
        this TimeSpan timeSpan,
        int precision = 1,
        CultureInfo? culture = null,
        TimeUnit maxUnit = TimeUnit.Year,
        TimeUnit minUnit = TimeUnit.Millisecond,
        string? collectionSeparator = null,
        bool countEmptyUnits = false)
    {
        culture ??= CultureInfo.CurrentCulture;

        var timeParts = CreateTimeParts(timeSpan, maxUnit, minUnit, culture);

        if (IsContainingOnlyNullValue(timeParts))
        {
            return CreateTimePartsWithNoTimeValue("no time")[0] ?? "no time";
        }

        timeParts = SetPrecisionOfTimeSpan(timeParts, precision, countEmptyUnits);

        return ConcatenateTimeSpanParts(timeParts, culture, collectionSeparator ?? ", ");
    }

    /// <summary>
    /// Breaks down a TimeSpan into its component time part strings for each unit
    /// between maxUnit and minUnit.
    /// </summary>
    private static List<string?> CreateTimeParts(
        TimeSpan timeSpan, TimeUnit maxUnit, TimeUnit minUnit, CultureInfo culture)
    {
        var timeParts = new List<string?>(8);
        var totalDays = Math.Abs(timeSpan.TotalDays);
        var totalHours = Math.Abs(timeSpan.TotalHours);
        var totalMinutes = Math.Abs(timeSpan.TotalMinutes);
        var totalSeconds = Math.Abs(timeSpan.TotalSeconds);
        var totalMilliseconds = Math.Abs(timeSpan.TotalMilliseconds);

        foreach (var unit in TimeUnits)
        {
            if (unit < minUnit || unit > maxUnit)
            {
                continue;
            }

            var value = unit switch
            {
                TimeUnit.Year => (int)(totalDays / DaysInAYear),
                TimeUnit.Month => (int)((totalDays % DaysInAYear) / DaysInAMonth),
                TimeUnit.Week => (int)((totalDays % DaysInAMonth) / DaysInAWeek),
                TimeUnit.Day => (int)(totalDays % DaysInAWeek),
                TimeUnit.Hour => timeSpan.Hours,
                TimeUnit.Minute => timeSpan.Minutes,
                TimeUnit.Second => timeSpan.Seconds,
                TimeUnit.Millisecond => timeSpan.Milliseconds,
                _ => 0
            };

            timeParts.Add(value != 0 ? FormatTimePart(value, unit, culture) : null);
        }

        return timeParts;
    }

    private static List<string?> CreateTimePartsWithNoTimeValue(string noTimeValue) =>
        [noTimeValue];

    private static bool IsContainingOnlyNullValue(IEnumerable<string?> timeParts) =>
        !timeParts.Any(x => x != null);

    /// <summary>
    /// Filters and limits time parts to the requested precision.
    /// </summary>
    private static List<string?> SetPrecisionOfTimeSpan(
        IEnumerable<string?> timeParts, int precision, bool countEmptyUnits)
    {
        // Anti-pattern: chained .Where().Take().Where() — 3 enumerator allocations
        // Plus [.. timeParts] collection expression allocates a final list
        if (!countEmptyUnits)
        {
            timeParts = timeParts.Where(x => x != null);
        }

        timeParts = timeParts.Take(precision);
        if (countEmptyUnits)
        {
            timeParts = timeParts.Where(x => x != null);
        }

        return [.. timeParts];
    }

    /// <summary>
    /// Joins time part strings with the specified separator.
    /// </summary>
    private static string ConcatenateTimeSpanParts(
        IEnumerable<string?> timeSpanParts, CultureInfo culture, string collectionSeparator)
    {
        var parts = timeSpanParts.Where(x => x != null).ToList();
        if (parts.Count == 0)
        {
            return "no time";
        }

        if (parts.Count == 1)
        {
            return parts[0]!;
        }

        var allButLast = string.Join(collectionSeparator, parts.Take(parts.Count - 1));
        return $"{allButLast} and {parts[^1]}";
    }

    private static string FormatTimePart(int value, TimeUnit unit, CultureInfo culture)
    {
        var unitName = unit.ToString().ToLowerInvariant();
        return value == 1 ? $"{value} {unitName}" : $"{value} {unitName}s";
    }
}

/// <summary>
/// Defines the contract for formatting a collection of items into a human-readable string.
/// </summary>
public interface ICollectionFormatter
{
    /// <summary>
    /// Formats a collection into a human-readable string using the default separator.
    /// </summary>
    /// <typeparam name="T">The type of items in the collection.</typeparam>
    /// <param name="collection">The collection to format.</param>
    /// <returns>A human-readable string representation of the collection.</returns>
    string Humanize<T>(IEnumerable<T> collection);

    /// <summary>
    /// Formats a collection using a custom object formatter and default separator.
    /// </summary>
    string Humanize<T>(IEnumerable<T> collection, Func<T, string?> objectFormatter);

    /// <summary>
    /// Formats a collection using a custom separator.
    /// </summary>
    string Humanize<T>(IEnumerable<T> collection, string separator);
}

/// <summary>
/// Default collection formatter that joins items with "and" for the last element.
/// Supports custom separators and object formatters.
/// </summary>
public class DefaultCollectionFormatter(string defaultSeparator) : ICollectionFormatter
{
    /// <summary>
    /// Gets the default separator used between collection items.
    /// </summary>
    protected string DefaultSeparator = defaultSeparator;

    /// <inheritdoc />
    public virtual string Humanize<T>(IEnumerable<T> collection) =>
        Humanize(collection, o => o?.ToString(), DefaultSeparator);

    /// <inheritdoc />
    public virtual string Humanize<T>(IEnumerable<T> collection, Func<T, string?> objectFormatter) =>
        Humanize(collection, objectFormatter, DefaultSeparator);

    /// <summary>
    /// Formats a collection using an object-returning formatter.
    /// </summary>
    public string Humanize<T>(IEnumerable<T> collection, Func<T, object?> objectFormatter) =>
        Humanize(collection, objectFormatter, DefaultSeparator);

    /// <inheritdoc />
    public virtual string Humanize<T>(IEnumerable<T> collection, string separator) =>
        Humanize(collection, o => o?.ToString(), separator);

    /// <summary>
    /// Core formatting method that converts a collection to a human-readable string.
    /// </summary>
    public virtual string Humanize<T>(
        IEnumerable<T> collection, Func<T, string?> objectFormatter, string separator)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(objectFormatter);

        // Anti-pattern: .Select().Where().ToArray() — allocates enumerator + delegate + array
        return HumanizeDisplayStrings(
            collection.Select(objectFormatter),
            separator);
    }

    /// <summary>
    /// Formats with an object-returning formatter, converting via .ToString().
    /// </summary>
    public string Humanize<T>(
        IEnumerable<T> collection, Func<T, object?> objectFormatter, string separator)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(objectFormatter);

        // Anti-pattern: double .Select() — Func<T,object> → .ToString() → string
        // Two Select calls = two enumerator allocations
        return HumanizeDisplayStrings(
            collection
                .Select(objectFormatter)
                .Select(o => o?.ToString()),
            separator);
    }

    private string HumanizeDisplayStrings(IEnumerable<string?> strings, string separator)
    {
        // Anti-pattern: .Select().Where().ToArray() chain
        var itemsArray = strings
            .Select(item => item == null ? string.Empty : item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

        if (itemsArray.Length == 0)
        {
            return string.Empty;
        }

        if (itemsArray.Length == 1)
        {
            return itemsArray[0];
        }

        var allButLast = string.Join(separator, itemsArray.Take(itemsArray.Length - 1));
        return $"{allButLast} and {itemsArray[^1]}";
    }
}

/// <summary>
/// Provides extension methods for transforming strings with params-based transformer chains.
/// </summary>
public static class TransformExtensions
{
    /// <summary>
    /// Transforms a string using the provided transformers, applied in order.
    /// </summary>
    /// <param name="input">The string to transform.</param>
    /// <param name="culture">The culture for culture-sensitive transformations.</param>
    /// <param name="transformers">One or more transformers to apply sequentially.</param>
    /// <returns>The transformed string.</returns>
    // Anti-pattern: params allocates a T[] on every call — add 1-arg overload for common case
    public static string Transform(
        this string input, CultureInfo culture, params IStringTransformer[] transformers) =>
        transformers.Aggregate(input, (current, t) => t.Transform(current));
}
