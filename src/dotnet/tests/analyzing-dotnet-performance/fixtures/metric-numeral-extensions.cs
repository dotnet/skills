using System.Collections.Frozen;

namespace Textualizer;

/// <summary>
/// Defines formatting options for metric numeral representations.
/// </summary>
[Flags]
public enum MetricFormat
{
    /// <summary>No special formatting applied.</summary>
    None = 0,

    /// <summary>Use the unit symbol instead of the full name (e.g., "k" instead of "kilo").</summary>
    UseSymbol = 1,

    /// <summary>Use short scale names (e.g., "billion" vs "milliard").</summary>
    UseShortScale = 2,

    /// <summary>Include the base unit name in the output.</summary>
    IncludeBaseUnit = 4
}

/// <summary>
/// Represents a metric unit prefix with its name, symbol, and scale words.
/// </summary>
internal struct UnitPrefix
{
    // Anti-pattern: struct does not implement IEquatable<UnitPrefix>
    // Boxing will occur on equality checks and when used as Dictionary key/value

    /// <summary>Gets the full name of this prefix (e.g., "kilo", "mega").</summary>
    public string Name { get; }

    /// <summary>Gets the short scale word (e.g., "thousand", "million").</summary>
    public string ShortScaleWord { get; }

    /// <summary>Gets the long scale word, falling back to short scale if not specified.</summary>
    public string LongScaleWord { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="UnitPrefix"/> struct.
    /// </summary>
    /// <param name="name">The prefix name.</param>
    /// <param name="shortScaleWord">The short scale word.</param>
    /// <param name="longScaleWord">The long scale word, or null to use short scale.</param>
    public UnitPrefix(string name, string shortScaleWord, string? longScaleWord = null)
    {
        Name = name;
        ShortScaleWord = shortScaleWord;
        LongScaleWord = longScaleWord ?? shortScaleWord;
    }
}

/// <summary>
/// Provides extension methods for converting numbers to and from metric numeral representations
/// (e.g., 1000 → "1k", "2.5M" → 2500000).
/// </summary>
public static class MetricNumeralExtensions
{
    // Correct: uses FrozenDictionary for the main prefix lookup — positive finding
    private static readonly FrozenDictionary<char, UnitPrefix> UnitPrefixes =
        new Dictionary<char, UnitPrefix>
        {
            ['k'] = new("kilo", "thousand"),
            ['M'] = new("mega", "million"),
            ['G'] = new("giga", "billion", "milliard"),
            ['T'] = new("tera", "trillion", "billion"),
            ['P'] = new("peta", "quadrillion", "billiard"),
            ['E'] = new("exa", "quintillion", "trillion"),
            ['Z'] = new("zetta", "sextillion", "trilliard"),
            ['Y'] = new("yotta", "septillion", "quadrillion"),
            ['m'] = new("milli", "thousandth"),
            ['μ'] = new("micro", "millionth"),
            ['n'] = new("nano", "billionth", "milliardth"),
            ['p'] = new("pico", "trillionth", "billionth"),
            ['f'] = new("femto", "quadrillionth", "billiardth"),
            ['a'] = new("atto", "quintillionth", "trillionth"),
            ['z'] = new("zepto", "sextillionth", "trilliardth"),
            ['y'] = new("yocto", "septillionth", "quadrillionth"),
        }.ToFrozenDictionary();

    /// <summary>
    /// Converts a number to its metric representation using the specified formatting options.
    /// </summary>
    /// <param name="input">The number to convert.</param>
    /// <param name="formats">Formatting options controlling symbol vs name and scale.</param>
    /// <param name="decimals">Optional number of decimal places to round to.</param>
    /// <returns>A string with the metric representation (e.g., "1.5k" or "1.5 kilo").</returns>
    /// <example>
    /// <code>
    /// 1500.ToMetric() => "1.5k"
    /// 1500.ToMetric(MetricFormat.None) => "1.5 kilo"
    /// 2500000.ToMetric(MetricFormat.UseSymbol) => "2.5M"
    /// </code>
    /// </example>
    public static string ToMetric(this double input, MetricFormat formats = MetricFormat.UseSymbol, int? decimals = null)
    {
        if (double.IsNaN(input) || double.IsInfinity(input))
        {
            return input.ToString();
        }

        if (Math.Abs(input) < 1e-24)
        {
            return "0";
        }

        var isNegative = input < 0;
        input = Math.Abs(input);

        var exponent = (int)Math.Floor(Math.Log10(input));
        var metricExponent = exponent - (exponent % 3);

        if (metricExponent == 0)
        {
            var formatted = decimals.HasValue ? input.ToString($"F{decimals}") : input.ToString("G");
            return isNegative ? $"-{formatted}" : formatted;
        }

        var symbol = GetSymbolForExponent(metricExponent);
        if (symbol == null)
        {
            var fallback = input.ToString("G");
            return isNegative ? $"-{fallback}" : fallback;
        }

        var scaledValue = input / Math.Pow(10, metricExponent);
        var valueStr = decimals.HasValue ? scaledValue.ToString($"F{decimals}") : scaledValue.ToString("G4");

        var suffix = formats.HasFlag(MetricFormat.UseSymbol)
            ? symbol.Value.ToString()
            : $" {GetPrefixName(symbol.Value, formats)}";

        return isNegative ? $"-{valueStr}{suffix}" : $"{valueStr}{suffix}";
    }

    /// <summary>
    /// Converts a metric string representation back to a number.
    /// </summary>
    /// <param name="input">The metric string to parse (e.g., "1.5k", "2.5 mega").</param>
    /// <returns>The numeric value represented by the metric string.</returns>
    /// <exception cref="ArgumentException">Thrown when the input is not a valid metric representation.</exception>
    /// <example>
    /// <code>
    /// "1.5k".FromMetric() => 1500
    /// "2.5M".FromMetric() => 2500000
    /// "100m".FromMetric() => 0.1
    /// </code>
    /// </example>
    public static double FromMetric(this string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        input = input.Trim();
        if (double.TryParse(input, out var directParse))
        {
            return directParse;
        }

        // Try symbol-based parsing first
        var lastChar = input[^1];
        if (UnitPrefixes.TryGetValue(lastChar, out var prefix))
        {
            var numericPart = input[..^1].Trim();
            if (double.TryParse(numericPart, out var value))
            {
                var exponent = GetExponentForSymbol(lastChar);
                return value * Math.Pow(10, exponent);
            }
        }

        // Try name-based parsing
        var nameInput = ReplaceNameBySymbol(input);
        if (nameInput != input)
        {
            return nameInput.FromMetric();
        }

        throw new ArgumentException($"Invalid metric numeral: '{input}'", nameof(input));
    }

    /// <summary>
    /// Determines whether the given string is a valid metric numeral.
    /// </summary>
    /// <param name="input">The string to validate.</param>
    /// <returns>true if the string can be parsed as a metric numeral; otherwise, false.</returns>
    public static bool IsValidMetricNumeral(this string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        input = input.Trim();
        if (double.TryParse(input, out _))
        {
            return true;
        }

        var index = input.Length - 1;
        var last = input[index];
        var isSymbol = UnitPrefixes.ContainsKey(last);
        return isSymbol && double.TryParse(input[..index], out _);
    }

    /// <summary>
    /// Replaces metric prefix names with their corresponding symbols in the input string.
    /// </summary>
    /// <param name="input">The string containing metric prefix names.</param>
    /// <returns>The string with prefix names replaced by symbols.</returns>
    private static string ReplaceNameBySymbol(string input) =>
        // Anti-pattern: .Aggregate() iterates ALL 16 prefixes, calling .Replace() each time
        // Creates up to 16 intermediate string allocations even when only one prefix is present
        UnitPrefixes.Aggregate(input, (current, unitPrefix) =>
            // Anti-pattern: char.ToString() allocates a string per iteration
            current.Replace(unitPrefix.Value.Name, unitPrefix.Key.ToString()));

    private static char? GetSymbolForExponent(int exponent)
    {
        foreach (var kvp in UnitPrefixes)
        {
            var exp = GetExponentForSymbol(kvp.Key);
            if (exp == exponent)
            {
                return kvp.Key;
            }
        }

        return null;
    }

    private static int GetExponentForSymbol(char symbol) =>
        symbol switch
        {
            'k' => 3, 'M' => 6, 'G' => 9, 'T' => 12, 'P' => 15, 'E' => 18, 'Z' => 21, 'Y' => 24,
            'm' => -3, 'μ' => -6, 'n' => -9, 'p' => -12, 'f' => -15, 'a' => -18, 'z' => -21, 'y' => -24,
            _ => 0
        };

    private static string GetPrefixName(char symbol, MetricFormat formats)
    {
        if (!UnitPrefixes.TryGetValue(symbol, out var prefix))
        {
            return symbol.ToString();
        }

        return formats.HasFlag(MetricFormat.UseShortScale) ? prefix.ShortScaleWord : prefix.LongScaleWord;
    }
}
