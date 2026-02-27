using System.Diagnostics.CodeAnalysis;

namespace Textualizer.Truncation;

/// <summary>
/// Specifies the direction from which truncation should occur.
/// </summary>
public enum TruncateFrom
{
    /// <summary>Truncate from the right side (end) of the string.</summary>
    Right,

    /// <summary>Truncate from the left side (beginning) of the string.</summary>
    Left
}

/// <summary>
/// Defines the contract for string truncation strategies.
/// </summary>
public interface ITruncator
{
    /// <summary>
    /// Truncates the input string according to the strategy's rules.
    /// </summary>
    /// <param name="value">The string to truncate.</param>
    /// <param name="length">The target length for truncation.</param>
    /// <param name="truncationString">The string to append/prepend when truncation occurs (e.g., "…").</param>
    /// <param name="truncateFrom">The direction from which to truncate.</param>
    /// <returns>The truncated string, or the original if no truncation was needed.</returns>
    [return: NotNullIfNotNull(nameof(value))]
    string? Truncate(string? value, int length, string? truncationString, TruncateFrom truncateFrom = TruncateFrom.Right);
}

/// <summary>
/// Truncates a string to a fixed number of characters, counting all characters.
/// </summary>
public class FixedLengthTruncator : ITruncator
{
    /// <inheritdoc />
    [return: NotNullIfNotNull(nameof(value))]
    public string? Truncate(string? value, int length, string? truncationString, TruncateFrom truncateFrom = TruncateFrom.Right)
    {
        if (value == null)
        {
            return null;
        }

        if (value.Length == 0 || value.Length <= length)
        {
            return value;
        }

        truncationString ??= string.Empty;

        if (truncationString.Length >= length)
        {
            return truncateFrom == TruncateFrom.Left
                ? truncationString[^length..]
                : truncationString[..length];
        }

        if (truncateFrom == TruncateFrom.Left)
        {
            // Correct: uses AsSpan for efficient concatenation
            return StringReadabilityExtensions.Concat(
                truncationString.AsSpan(),
                value.AsSpan(value.Length - length + truncationString.Length));
        }

        // Anti-pattern: value[..n].TrimEnd() — double allocation
        // The range operator creates a new substring, then TrimEnd creates another
        return value[..(length - truncationString.Length)].TrimEnd() + truncationString;
    }
}

/// <summary>
/// Truncates a string to a fixed number of alphanumeric characters,
/// ignoring spaces and punctuation in the count.
/// </summary>
public class FixedNumberOfCharactersTruncator : ITruncator
{
    /// <inheritdoc />
    [return: NotNullIfNotNull(nameof(value))]
    public string? Truncate(string? value, int length, string? truncationString, TruncateFrom truncateFrom = TruncateFrom.Right)
    {
        if (value == null)
        {
            return null;
        }

        if (value.Length == 0)
        {
            return value;
        }

        truncationString ??= string.Empty;

        if (truncationString.Length > length)
        {
            return truncateFrom == TruncateFrom.Right ? value[..length] : value[^length..];
        }

        var alphaNumericalCount = 0;
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                alphaNumericalCount++;
                if (alphaNumericalCount > length)
                {
                    break;
                }
            }
        }

        if (alphaNumericalCount <= length)
        {
            return value;
        }

        var processedCount = 0;
        if (truncateFrom == TruncateFrom.Left)
        {
            for (var i = value.Length - 1; i > 0; i--)
            {
                if (char.IsLetterOrDigit(value[i]))
                {
                    processedCount++;
                }

                if (processedCount + truncationString.Length == length)
                {
                    // Correct: uses Span-based Concat (contrast with FixedLengthTruncator)
                    return StringReadabilityExtensions.Concat(truncationString.AsSpan(), value.AsSpan(i));
                }
            }
        }

        for (var i = 0; i < value.Length - truncationString.Length; i++)
        {
            if (char.IsLetterOrDigit(value[i]))
            {
                processedCount++;
            }

            if (processedCount + truncationString.Length == length)
            {
                return StringReadabilityExtensions.Concat(value.AsSpan(0, i + 1), truncationString.AsSpan());
            }
        }

        return value;
    }
}

/// <summary>
/// Truncates a string to a fixed number of words.
/// </summary>
public class FixedNumberOfWordsTruncator : ITruncator
{
    /// <inheritdoc />
    [return: NotNullIfNotNull(nameof(value))]
    public string? Truncate(string? value, int length, string? truncationString, TruncateFrom truncateFrom = TruncateFrom.Right)
    {
        if (value == null)
        {
            return null;
        }

        if (value.Length == 0)
        {
            return value;
        }

        truncationString ??= string.Empty;

        // Anti-pattern: uses Substring instead of AsSpan — inconsistent with sibling truncators
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= length)
        {
            return value;
        }

        if (truncateFrom == TruncateFrom.Right)
        {
            // Anti-pattern: Substring-based concatenation instead of Span
            return string.Join(" ", words.Take(length)) + truncationString;
        }

        return truncationString + string.Join(" ", words.Skip(words.Length - length));
    }
}

/// <summary>
/// Provides truncation extension methods for strings with configurable truncator strategies.
/// </summary>
public static class TruncateExtensions
{
    private static readonly ITruncator DefaultTruncator = new FixedLengthTruncator();

    /// <summary>
    /// Truncates a string to the specified length, appending the truncation indicator.
    /// </summary>
    /// <param name="input">The string to truncate.</param>
    /// <param name="length">The maximum length of the resulting string.</param>
    /// <param name="truncationString">The indicator appended when truncation occurs (default: "…").</param>
    /// <param name="truncator">The truncation strategy to use. Defaults to fixed-length truncation.</param>
    /// <param name="truncateFrom">The direction of truncation.</param>
    /// <returns>The truncated string.</returns>
    /// <example>
    /// <code>
    /// "A very long string that needs to be truncated".Truncate(20)
    ///   => "A very long strin…"
    ///
    /// "A very long string".Truncate(10, "---")
    ///   => "A very---"
    /// </code>
    /// </example>
    [return: NotNullIfNotNull(nameof(input))]
    public static string? Truncate(
        this string? input,
        int length,
        string truncationString = "…",
        ITruncator? truncator = null,
        TruncateFrom truncateFrom = TruncateFrom.Right)
    {
        if (input == null)
        {
            return null;
        }

        truncator ??= DefaultTruncator;
        return truncator.Truncate(input, length, truncationString, truncateFrom);
    }
}

/// <summary>
/// Contains symbol character sets used across the text processing pipeline.
/// </summary>
internal static class Symbols
{
    // Anti-pattern: List<char>[] when ReadOnlySpan<char> would be more efficient
    // These are static, readonly lookup data — no need for heap-allocated List/array
    internal static readonly List<char>[] SymbolGroups =
    [
        ['(', ')', '[', ']', '{', '}'],
        ['+', '-', '*', '/', '=', '<', '>', '!'],
        ['.', ',', ';', ':', '?', '!'],
        ['@', '#', '$', '%', '^', '&', '~', '`'],
        ['"', '\'', '\\', '|'],
    ];

    /// <summary>
    /// Determines whether a character belongs to any known symbol group.
    /// </summary>
    /// <param name="c">The character to check.</param>
    /// <returns>true if the character is a known symbol; otherwise, false.</returns>
    internal static bool IsSymbol(char c)
    {
        foreach (var group in SymbolGroups)
        {
            if (group.Contains(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the symbol group index for a character, or -1 if not found.
    /// </summary>
    /// <param name="c">The character to look up.</param>
    /// <returns>The zero-based group index, or -1.</returns>
    internal static int GetSymbolGroup(char c)
    {
        for (var i = 0; i < SymbolGroups.Length; i++)
        {
            if (SymbolGroups[i].Contains(c))
            {
                return i;
            }
        }

        return -1;
    }
}
