using System.Text.RegularExpressions;

namespace Textualizer;

/// <summary>
/// Provides extension methods for converting strings between human-readable form
/// and PascalCase/camelCase representations.
/// </summary>
public static partial class StringReadabilityExtensions
{
#if NET7_0_OR_GREATER
    [GeneratedRegex(@"[\p{Lu}]+(?=\p{Lu}[\p{Ll}])|[\p{Lu}]?[\p{Ll}]+|\d+")]
    private static partial Regex PascalCaseWordPartsRegexGenerated();

    [GeneratedRegex(@"^[\p{Zs}\t]+$")]
    private static partial Regex FreestandingSpacingCharRegexGenerated();

    private static Regex PascalCaseWordPartsRegex() => PascalCaseWordPartsRegexGenerated();
    private static Regex FreestandingSpacingCharRegex() => FreestandingSpacingCharRegexGenerated();
#else
    private static readonly Regex PascalCaseWordPartsRegexField =
        new(@"[\p{Lu}]+(?=\p{Lu}[\p{Ll}])|[\p{Lu}]?[\p{Ll}]+|\d+", RegexOptions.Compiled);
    private static readonly Regex FreestandingSpacingCharRegexField =
        new(@"^[\p{Zs}\t]+$", RegexOptions.Compiled);

    private static Regex PascalCaseWordPartsRegex() => PascalCaseWordPartsRegexField;
    private static Regex FreestandingSpacingCharRegex() => FreestandingSpacingCharRegexField;
#endif

    /// <summary>
    /// Converts a machine-readable string (PascalCase, camelCase, snake_case, kebab-case)
    /// to a human-readable form with spaces between words.
    /// </summary>
    /// <param name="input">The string to humanize.</param>
    /// <returns>A human-readable version of the input string.</returns>
    /// <example>
    /// <code>
    /// "PascalCaseInputStringIsTurnedIntoSentence".Humanize()
    ///   => "Pascal case input string is turned into sentence"
    ///
    /// "Underscored_input_String_is_turned_into_sentence".Humanize()
    ///   => "Underscored input string is turned into sentence"
    ///
    /// "HTMLParser".Humanize() => "HTML parser"
    /// </code>
    /// </example>
    public static string Humanize(this string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        // Handle already-spaced strings
        if (FreestandingSpacingCharRegex().IsMatch(input))
        {
            return input;
        }

        // Handle underscore/dash separated
        if (input.Contains('_') || input.Contains('-'))
        {
            return FromUnderscoreDashSeparatedWords(input);
        }

        return FromPascalCase(input);
    }

    /// <summary>
    /// Converts a string from underscore or dash separation to a human-readable form.
    /// </summary>
    /// <param name="input">The underscore or dash separated string.</param>
    /// <returns>A space-separated human-readable string.</returns>
    internal static string FromUnderscoreDashSeparatedWords(string input) =>
        string.Join(" ", input.Split(['_', '-']));

    /// <summary>
    /// Converts a PascalCase or camelCase string to a human-readable form.
    /// Preserves acronyms (consecutive uppercase letters) as-is.
    /// </summary>
    /// <param name="input">The PascalCase string to convert.</param>
    /// <returns>A space-separated human-readable string.</returns>
    internal static string FromPascalCase(string input)
    {
        // Anti-pattern: LINQ chain .Cast<Match>().Select() — allocates enumerator + delegate
        var result = string.Join(" ", PascalCaseWordPartsRegex()
            .Matches(input)
            .Cast<Match>()
            .Select(match =>
            {
                var value = match.Value;
                // Anti-pattern: .All(char.IsUpper) — LINQ on string allocates enumerator
                return value.All(char.IsUpper) &&
                       (value.Length > 1 || (match.Index > 0 && input[match.Index - 1] == ' ') || value == "I")
                    ? value
                    // Anti-pattern: .ToLower() without culture inside lambda — per-match allocation
                    : value.ToLower();
            }));

        // Anti-pattern: .All() again and .Contains without StringComparison
        if (result.All(c => c == ' ' || char.IsUpper(c)) && result.Contains(' '))
        {
            result = result.ToLower();
        }

        return result.Length > 0 ? char.ToUpper(result[0]) + result[1..] : result;
    }

    /// <summary>
    /// Concatenates two <see cref="ReadOnlySpan{T}"/> of <see cref="char"/> into a new string.
    /// Uses efficient span-based concatenation to avoid intermediate allocations.
    /// </summary>
    /// <param name="first">The first span of characters.</param>
    /// <param name="second">The second span of characters.</param>
    /// <returns>A new string containing the concatenated characters.</returns>
    internal static string Concat(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
    {
        // Correct: uses Span-based concatenation — positive finding
        return string.Concat(first, second);
    }

    /// <summary>
    /// Concatenates a single character with a <see cref="ReadOnlySpan{T}"/> of characters.
    /// </summary>
    /// <param name="c">The character to prepend.</param>
    /// <param name="rest">The remaining characters.</param>
    /// <returns>A new string with the character prepended.</returns>
    internal static string Concat(char c, ReadOnlySpan<char> rest)
    {
        Span<char> buffer = stackalloc char[1 + rest.Length];
        buffer[0] = c;
        rest.CopyTo(buffer[1..]);
        return new string(buffer);
    }
}

/// <summary>
/// Provides extension methods for converting human-readable strings back to PascalCase.
/// </summary>
public static class StringDehumanizeExtensions
{
    /// <summary>
    /// Converts a humanized string back to PascalCase by capitalizing each word and removing spaces.
    /// </summary>
    /// <param name="input">The humanized string to dehumanize.</param>
    /// <returns>
    /// A PascalCase string. If the input is already PascalCase (no spaces), it is returned unchanged.
    /// </returns>
    /// <example>
    /// <code>
    /// "some string".Dehumanize() => "SomeString"
    /// "Some String".Dehumanize() => "SomeString"
    /// "SomeStringAndAnotherString".Dehumanize() => "SomeStringAndAnotherString"
    /// </code>
    /// </example>
    public static string Dehumanize(this string input)
    {
        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return input;
        }

        if (words.Length == 1)
        {
            return words[0].Humanize().Pascalize();
        }

        // Anti-pattern: string.Concat(words.Select(...)) — LINQ allocates enumerator + delegate
        // Each word goes through Humanize() then Pascalize() — double processing
        return string.Concat(words.Select(word => word.Humanize().Pascalize()));
    }

    /// <summary>
    /// Converts a string to PascalCase by capitalizing the first letter of each word
    /// and removing separators.
    /// </summary>
    /// <param name="input">The string to pascalize.</param>
    /// <returns>A PascalCase representation of the input.</returns>
    public static string Pascalize(this string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        return input.Humanize().Transform(To.TitleCase).Replace(" ", "");
    }
}

/// <summary>
/// Provides string transformation capabilities using the strategy pattern.
/// </summary>
public interface IStringTransformer
{
    /// <summary>Transforms the input string.</summary>
    string Transform(string input);
}

/// <summary>
/// A portal to string transformation using IStringTransformer.
/// </summary>
public static class To
{
    /// <summary>
    /// Transforms a string using the provided transformers, applied in order.
    /// </summary>
    /// <param name="input">The string to transform.</param>
    /// <param name="transformers">One or more transformers to apply sequentially.</param>
    /// <returns>The transformed string.</returns>
    // Anti-pattern: params allocates a T[] on every call, even for the common 1-arg case
    // Should add 1-arg and 2-arg overloads to avoid the array allocation
    public static string Transform(this string input, params IStringTransformer[] transformers) =>
        transformers.Aggregate(input, (current, t) => t.Transform(current));

    /// <summary>Changes string to title case.</summary>
    public static IStringTransformer TitleCase { get; } = new ToTitleCase();

    /// <summary>Changes the string to lower case.</summary>
    public static IStringTransformer LowerCase { get; } = new ToLowerCase();

    /// <summary>Changes the string to upper case.</summary>
    public static IStringTransformer UpperCase { get; } = new ToUpperCase();
}

internal class ToTitleCase : IStringTransformer
{
    public string Transform(string input) =>
        System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input);
}

internal class ToLowerCase : IStringTransformer
{
    public string Transform(string input) => input.ToLowerInvariant();
}

internal class ToUpperCase : IStringTransformer
{
    public string Transform(string input) => input.ToUpperInvariant();
}
