using System.Text.RegularExpressions;

namespace Textualizer;

/// <summary>
/// Provides extension methods for transforming strings between different casing conventions.
/// Supports PascalCase, camelCase, snake_case, kebab-case, and Title Case conversions.
/// </summary>
public static partial class CaseTransformExtensions
{
    // --- Correct patterns (GeneratedRegex for .NET 7+) ---
#if NET7_0_OR_GREATER
    [GeneratedRegex(@"[-\s]")]
    private static partial Regex SeparatorRegexGenerated();

    [GeneratedRegex(@"(?<=[a-z])(?=[A-Z])")]
    private static partial Regex CamelCaseBoundaryGenerated();

    [GeneratedRegex(@"[\p{Lu}]+(?=\p{Lu}[\p{Ll}])|[\p{Lu}]?[\p{Ll}]+|\d+")]
    private static partial Regex WordPartsRegexGenerated();

    private static Regex SeparatorRegex() => SeparatorRegexGenerated();
    private static Regex CamelCaseBoundary() => CamelCaseBoundaryGenerated();
    private static Regex WordPartsRegex() => WordPartsRegexGenerated();
#else
    private static readonly Regex SeparatorRegexField = new(@"[-\s]", RegexOptions.Compiled);
    private static readonly Regex CamelCaseBoundaryField = new(@"(?<=[a-z])(?=[A-Z])", RegexOptions.Compiled);
    private static readonly Regex WordPartsRegexField = new(@"[\p{Lu}]+(?=\p{Lu}[\p{Ll}])|[\p{Lu}]?[\p{Ll}]+|\d+", RegexOptions.Compiled);

    private static Regex SeparatorRegex() => SeparatorRegexField;
    private static Regex CamelCaseBoundary() => CamelCaseBoundaryField;
    private static Regex WordPartsRegex() => WordPartsRegexField;
#endif

    // --- Anti-pattern: three compiled regex fields for underscore transformation ---
    private static readonly Regex UnderscoreRegex1 = new(@"([A-Z]+)([A-Z][a-z])", RegexOptions.Compiled);
    private static readonly Regex UnderscoreRegex2 = new(@"([a-z\d])([A-Z])", RegexOptions.Compiled);
    private static readonly Regex UnderscoreRegex3 = new(@"[-\s]", RegexOptions.Compiled);

    /// <summary>
    /// Converts a PascalCase or camelCase string to a human-readable title.
    /// </summary>
    /// <param name="input">The PascalCase or camelCase string to convert.</param>
    /// <returns>A human-readable string with words separated by spaces.</returns>
    /// <example>
    /// <code>
    /// "SomePropertyName".Titleize() => "Some Property Name"
    /// "firstName".Titleize() => "First Name"
    /// </code>
    /// </example>
    public static string Titleize(this string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        return CamelCaseBoundary()
            .Replace(input, " ")
            .Replace('_', ' ')
            .Trim();
    }

    /// <summary>
    /// Converts a string to PascalCase by capitalizing the first letter of each word.
    /// </summary>
    /// <param name="input">The string to convert.</param>
    /// <returns>A PascalCase representation of the input string.</returns>
    /// <example>
    /// <code>
    /// "some_property_name".Pascalize() => "SomePropertyName"
    /// "some-property-name".Pascalize() => "SomePropertyName"
    /// </code>
    /// </example>
    public static string Pascalize(this string input)
    {
        // Anti-pattern: .Value.ToUpper() per regex match allocates a string per match
        return WordPartsRegex().Replace(input, match =>
            match.Value.Substring(0, 1).ToUpper() + match.Value.Substring(1));
    }

    /// <summary>
    /// Converts a string to camelCase.
    /// </summary>
    /// <param name="input">The string to convert.</param>
    /// <returns>A camelCase representation of the input string.</returns>
    /// <example>
    /// <code>
    /// "SomePropertyName".Camelize() => "somePropertyName"
    /// "some_property_name".Camelize() => "somePropertyName"
    /// </code>
    /// </example>
    public static string Camelize(this string input)
    {
        var pascalized = input.Pascalize();
        if (pascalized.Length == 0)
        {
            return pascalized;
        }

        return char.ToLowerInvariant(pascalized[0]) + pascalized[1..];
    }

    /// <summary>
    /// Converts a PascalCase or camelCase string to snake_case.
    /// </summary>
    /// <param name="input">The string to convert.</param>
    /// <returns>A snake_case representation of the input string.</returns>
    /// <example>
    /// <code>
    /// "SomePropertyName".Underscore() => "some_property_name"
    /// "HTMLParser".Underscore() => "html_parser"
    /// </code>
    /// </example>
    public static string Underscore(this string input) =>
        // Anti-pattern: triple regex Replace chain + .ToLower() = 4 allocations per call
        UnderscoreRegex3
            .Replace(
                UnderscoreRegex2.Replace(
                    UnderscoreRegex1.Replace(input, "$1_$2"), "$1_$2"), "_")
            .ToLower(); // Anti-pattern: .ToLower() without culture

    /// <summary>
    /// Replaces all underscores in the string with dashes (hyphens).
    /// </summary>
    /// <param name="input">The underscored string to convert.</param>
    /// <returns>A string with underscores replaced by dashes.</returns>
    /// <example>
    /// <code>
    /// "some_property_name".Dasherize() => "some-property-name"
    /// </code>
    /// </example>
    public static string Dasherize(this string input) =>
        input.Replace('_', '-');

    /// <summary>
    /// Converts a string to kebab-case by underscoring and then dasherizing.
    /// </summary>
    /// <param name="input">The string to convert.</param>
    /// <returns>A kebab-case representation of the input string.</returns>
    /// <example>
    /// <code>
    /// "SomePropertyName".Kebaberize() => "some-property-name"
    /// "somePropertyName".Kebaberize() => "some-property-name"
    /// </code>
    /// </example>
    public static string Kebaberize(this string input) =>
        Underscore(input)
            .Dasherize();

    /// <summary>
    /// Determines whether the given string is entirely uppercase.
    /// </summary>
    /// <param name="input">The string to check.</param>
    /// <returns>true if all characters in the string are uppercase; otherwise, false.</returns>
    public static bool IsAllUpperCase(this string input)
    {
        // Correct pattern: uses foreach loop instead of LINQ
        foreach (var c in input)
        {
            if (char.IsLetter(c) && !char.IsUpper(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Converts a string to a URL-friendly slug.
    /// </summary>
    /// <param name="input">The string to slugify.</param>
    /// <returns>A URL-friendly slug representation.</returns>
    /// <example>
    /// <code>
    /// "Hello World!".Slugify() => "hello-world"
    /// "  Some  Title  ".Slugify() => "some-title"
    /// </code>
    /// </example>
    public static string Slugify(this string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        // Correct: uses ToLowerInvariant instead of ToLower
        var normalized = input.Trim().ToLowerInvariant();
        normalized = SeparatorRegex().Replace(normalized, "-");

        return normalized;
    }
}

/// <summary>
/// Represents an inflection rule that uses a regex pattern to match and transform words.
/// Used by the vocabulary system to apply pluralization and singularization rules.
/// </summary>
internal class InflectionRule
{
    private readonly Regex _regex;
    private readonly string _replacement;

    /// <summary>
    /// Initializes a new instance of the <see cref="InflectionRule"/> class.
    /// </summary>
    /// <param name="pattern">The regex pattern to match against input words.</param>
    /// <param name="replacement">The replacement string with regex group references.</param>
    public InflectionRule(string pattern, string replacement)
    {
        // Anti-pattern: new Regex with RegexOptions.Compiled per rule instance.
        // If 40-60 rules are created at startup, this is 40-60 compiled regexes
        // consuming 100-500ms of startup time for matching short strings.
        _regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        _replacement = replacement;
    }

    /// <summary>
    /// Attempts to apply this rule to the given word.
    /// </summary>
    /// <param name="word">The word to transform.</param>
    /// <returns>The transformed word if the pattern matches; otherwise, null.</returns>
    public string? Apply(string word)
    {
        if (!_regex.IsMatch(word))
        {
            return null;
        }

        return _regex.Replace(word, _replacement);
    }
}

/// <summary>
/// Provides common string validation utilities used across the transformation pipeline.
/// </summary>
public static class StringValidation
{
    /// <summary>
    /// Checks whether a string contains only ASCII letters and digits.
    /// </summary>
    /// <param name="input">The string to validate.</param>
    /// <returns>true if the string contains only ASCII alphanumeric characters; otherwise, false.</returns>
    public static bool IsAsciiAlphanumeric(this string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        foreach (var c in input)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the number of words in a string, splitting on whitespace and common delimiters.
    /// </summary>
    /// <param name="input">The string to count words in.</param>
    /// <returns>The number of words found.</returns>
    public static int WordCount(this string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return 0;
        }

        var count = 0;
        var inWord = false;
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (!inWord)
                {
                    count++;
                    inWord = true;
                }
            }
            else
            {
                inWord = false;
            }
        }

        return count;
    }
}
