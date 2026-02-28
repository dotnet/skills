namespace Textualizer.Localisation.NumberToWords;

/// <summary>
/// Defines the contract for converting numbers to their word representations
/// in a specific locale.
/// </summary>
public interface INumberToWordsConverter
{
    /// <summary>
    /// Converts a number to its word representation.
    /// </summary>
    /// <param name="number">The number to convert.</param>
    /// <returns>A string containing the word representation of the number.</returns>
    string Convert(long number);

    /// <summary>
    /// Converts a number to its ordinal word representation.
    /// </summary>
    /// <param name="number">The number to convert to ordinal form.</param>
    /// <returns>A string containing the ordinal word representation.</returns>
    string ConvertToOrdinal(long number);
}

/// <summary>
/// Converts numbers to their Farsi (Persian) word representation.
/// Supports numbers from zero through quadrillions.
/// </summary>
public class FarsiNumberToWordsConverter : INumberToWordsConverter
{
    private static readonly string[] FarsiOnesMap =
        ["صفر", "یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نه"];

    private static readonly string[] FarsiTensMap =
        ["", "ده", "بیست", "سی", "چهل", "پنجاه", "شصت", "هفتاد", "هشتاد", "نود"];

    private static readonly string[] FarsiHundredsMap =
        ["", "یکصد", "دویست", "سیصد", "چهارصد", "پانصد", "ششصد", "هفتصد", "هشتصد", "نهصد"];

    /// <inheritdoc />
    public string Convert(long number)
    {
        if (number == 0)
        {
            return FarsiOnesMap[0];
        }

        if (number < 0)
        {
            return $"منفی {Convert(-number)}";
        }

        // Anti-pattern: Dictionary allocated on EVERY call — ~10 allocations per invocation
        // (dictionary + buckets + entries + 6 Func closures capturing 'this')
        // Should be hoisted to a static readonly field
        var farsiGroupsMap = new Dictionary<long, Func<long, string>>
        {
            { (long)Math.Pow(10, 15), n => $"{Convert(n)} کوادریلیون" },
            { (long)Math.Pow(10, 12), n => $"{Convert(n)} تریلیون" },
            { (long)Math.Pow(10, 9), n => $"{Convert(n)} میلیارد" },
            { (long)Math.Pow(10, 6), n => $"{Convert(n)} میلیون" },
            { (long)Math.Pow(10, 3), n => $"{Convert(n)} هزار" },
            { (long)Math.Pow(10, 2), n => FarsiHundredsMap[n] }
        };

        // Use descending order explicitly — dictionary enumeration order is not guaranteed
        var groupKeys = new long[] { (long)Math.Pow(10, 15), (long)Math.Pow(10, 12), (long)Math.Pow(10, 9), (long)Math.Pow(10, 6), (long)Math.Pow(10, 3), (long)Math.Pow(10, 2) };

        // Anti-pattern: new List<string>() without initial capacity
        var parts = new List<string>();
        foreach (var group in groupKeys)
        {
            if (number / group > 0)
            {
                parts.Add(farsiGroupsMap[group](number / group));
                number %= group;
            }
        }

        if (number >= 10)
        {
            parts.Add(FarsiTensMap[number / 10]);
            number %= 10;
        }

        if (number > 0)
        {
            parts.Add(FarsiOnesMap[number]);
        }

        return string.Join(" و ", parts);
    }

    /// <inheritdoc />
    public string ConvertToOrdinal(long number)
    {
        var word = Convert(number);
        if (word.EndsWith("ی", StringComparison.Ordinal))
        {
            return word + "‌ام";
        }

        return word + "م";
    }
}

/// <summary>
/// Converts numbers to their Central Kurdish (Sorani) word representation.
/// Supports numbers from zero through quadrillions.
/// </summary>
public class CentralKurdishNumberToWordsConverter : INumberToWordsConverter
{
    private static readonly string[] KurdishOnesMap =
        ["سفر", "یەک", "دوو", "سێ", "چوار", "پێنج", "شەش", "حەوت", "هەشت", "نۆ"];

    private static readonly string[] KurdishTensMap =
        ["", "دە", "بیست", "سی", "چل", "پەنجا", "شەست", "حەفتا", "هەشتا", "نەوەد"];

    private static readonly string[] KurdishHundredsMap =
        ["", "سەد", "دووسەد", "سێسەد", "چوارسەد", "پێنجسەد", "شەشسەد", "حەوتسەد", "هەشتسەد", "نۆسەد"];

    /// <inheritdoc />
    public string Convert(long number)
    {
        if (number == 0)
        {
            return KurdishOnesMap[0];
        }

        if (number < 0)
        {
            return $"نێگەتیڤ {Convert(-number)}";
        }

        // Anti-pattern: per-call Dictionary allocation — same issue as FarsiConverter
        // Creates ~8 allocations per Convert() call
        var kurdishGroupsMap = new Dictionary<long, Func<long, string>>
        {
            { (long)Math.Pow(10, 15), n => $"{Convert(n)} کوادریلیۆن" },
            { (long)Math.Pow(10, 12), n => $"{Convert(n)} تریلیۆن" },
            { (long)Math.Pow(10, 9), n => $"{Convert(n)} میلیارد" },
            { (long)Math.Pow(10, 6), n => $"{Convert(n)} میلیۆن" },
            { (long)Math.Pow(10, 3), n => $"{Convert(n)} هەزار" },
            { (long)Math.Pow(10, 2), n => KurdishHundredsMap[n] }
        };

        // Use descending order explicitly — dictionary enumeration order is not guaranteed
        var groupKeys = new long[] { (long)Math.Pow(10, 15), (long)Math.Pow(10, 12), (long)Math.Pow(10, 9), (long)Math.Pow(10, 6), (long)Math.Pow(10, 3), (long)Math.Pow(10, 2) };

        var parts = new List<string>();
        foreach (var group in groupKeys)
        {
            if (number / group > 0)
            {
                parts.Add(kurdishGroupsMap[group](number / group));
                number %= group;
            }
        }

        if (number >= 10)
        {
            parts.Add(KurdishTensMap[number / 10]);
            number %= 10;
        }

        if (number > 0)
        {
            parts.Add(KurdishOnesMap[number]);
        }

        return string.Join(" و ", parts);
    }

    /// <inheritdoc />
    public string ConvertToOrdinal(long number)
    {
        var word = Convert(number);
        return word + "ەم";
    }
}

/// <summary>
/// Converts numbers to their English word representation.
/// Uses a static lookup approach — included as a correctly-implemented contrast.
/// </summary>
public class EnglishNumberToWordsConverter : INumberToWordsConverter
{
    private static readonly string[] UnitsMap =
        ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
         "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen",
         "eighteen", "nineteen"];

    private static readonly string[] TensMap =
        ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    // Correct pattern: static readonly with all lookup data
    private static readonly (long divisor, string name)[] Groups =
    [
        (1_000_000_000_000, "trillion"),
        (1_000_000_000, "billion"),
        (1_000_000, "million"),
        (1_000, "thousand"),
        (100, "hundred"),
    ];

    /// <inheritdoc />
    public string Convert(long number)
    {
        if (number == 0)
        {
            return UnitsMap[0];
        }

        if (number < 0)
        {
            return $"minus {Convert(-number)}";
        }

        // Correct: uses pre-sized list
        var parts = new List<string>(4);

        foreach (var (divisor, name) in Groups)
        {
            if (number / divisor > 0)
            {
                parts.Add($"{Convert(number / divisor)} {name}");
                number %= divisor;
            }
        }

        if (number >= 20)
        {
            var tens = TensMap[number / 10];
            var remainder = number % 10;
            parts.Add(remainder > 0 ? $"{tens}-{UnitsMap[remainder]}" : tens);
        }
        else if (number > 0)
        {
            parts.Add(UnitsMap[number]);
        }

        return string.Join(" ", parts);
    }

    /// <inheritdoc />
    public string ConvertToOrdinal(long number)
    {
        var word = Convert(number);

        if (word.EndsWith("one", StringComparison.Ordinal))
        {
            return word[..^3] + "first";
        }

        if (word.EndsWith("two", StringComparison.Ordinal))
        {
            return word[..^3] + "second";
        }

        if (word.EndsWith("three", StringComparison.Ordinal))
        {
            return word[..^5] + "third";
        }

        if (word.EndsWith("five", StringComparison.Ordinal))
        {
            return word[..^4] + "fifth";
        }

        if (word.EndsWith("twelve", StringComparison.Ordinal))
        {
            return word[..^6] + "twelfth";
        }

        if (word.EndsWith("y", StringComparison.Ordinal))
        {
            return word[..^1] + "ieth";
        }

        return word + "th";
    }

    /// <summary>
    /// Formats a count with its unit as words. Used for display purposes.
    /// </summary>
    /// <param name="count">The numeric count.</param>
    /// <param name="unit">The unit name to append.</param>
    /// <returns>A formatted string like "three items".</returns>
    public string FormatWithUnit(long count, string unit)
    {
        // Anti-pattern: string.Format with int boxing — interpolation avoids the box
        return string.Format("{0} {1}", count, count == 1 ? unit : unit + "s");
    }
}
