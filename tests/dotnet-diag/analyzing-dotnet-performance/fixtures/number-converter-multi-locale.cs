using System.Collections.Frozen;

namespace Textualizer.Localisation.NumberToWords;

/// <summary>
/// Converts numbers to their Swedish word representation.
/// Handles cardinal and ordinal forms with proper gender agreement.
/// </summary>
public class SwedishNumberToWordsConverter : INumberToWordsConverter
{
    private static readonly string[] UnitsMap =
        ["noll", "ett", "två", "tre", "fyra", "fem", "sex", "sju", "åtta", "nio",
         "tio", "elva", "tolv", "tretton", "fjorton", "femton", "sexton", "sjutton",
         "arton", "nitton"];

    private static readonly string[] TensMap =
        ["", "tio", "tjugo", "trettio", "fyrtio", "femtio", "sextio", "sjuttio", "åttio", "nittio"];

    // Anti-pattern: static readonly Dictionary not using FrozenDictionary
    // These are initialized once and never mutated — FrozenDictionary gives ~50% faster lookups
    private static readonly Dictionary<long, string> OrdinalExceptions = new()
    {
        { 1, "första" },
        { 2, "andra" },
        { 3, "tredje" },
        { 4, "fjärde" },
        { 5, "femte" },
        { 6, "sjätte" },
        { 7, "sjunde" },
        { 8, "åttonde" },
        { 9, "nionde" },
        { 10, "tionde" },
        { 11, "elfte" },
        { 12, "tolfte" }
    };

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

        var word = "";

        if (number / 1_000_000_000 > 0)
        {
            word += Convert(number / 1_000_000_000) + " miljarder ";
            number %= 1_000_000_000;
        }

        if (number / 1_000_000 > 0)
        {
            word += Convert(number / 1_000_000) + " miljoner ";
            number %= 1_000_000;
        }

        if (number / 1_000 > 0)
        {
            // Anti-pattern: string += concatenation in branches
            if (number / 1_000 == 1)
            {
                word += "ettusen ";
            }
            else
            {
                word += Convert(number / 1_000) + "tusen ";
            }

            number %= 1_000;
        }

        if (number / 100 > 0)
        {
            if (number / 100 == 1)
            {
                word += "etthundra";
            }
            else
            {
                word += Convert(number / 100) + "hundra";
            }

            number %= 100;
        }

        if (number > 0)
        {
            if (word != "")
            {
                word += "och";
            }

            if (number < 20)
            {
                word += UnitsMap[number];
            }
            else
            {
                word += TensMap[number / 10];
                if (number % 10 > 0)
                {
                    word += UnitsMap[number % 10];
                }
            }
        }

        return word.Trim();
    }

    /// <inheritdoc />
    public string ConvertToOrdinal(long number)
    {
        if (OrdinalExceptions.TryGetValue(number, out var exception))
        {
            return exception;
        }

        var word = Convert(number);

        // Anti-pattern: .EndsWith without StringComparison on ASCII literal
        if (word.EndsWith("tre"))
        {
            return word + "dje";
        }

        // Anti-pattern: .EndsWith without StringComparison
        if (word.EndsWith("a") || word.EndsWith("e"))
        {
            return word + "nde";
        }

        return word + "de";
    }
}

/// <summary>
/// Converts numbers to their French word representation.
/// Base class for France French, Belgian French, and Swiss French variants.
/// </summary>
public abstract class FrenchNumberToWordsConverterBase : INumberToWordsConverter
{
    private static readonly string[] UnitsMap =
        ["zéro", "un", "deux", "trois", "quatre", "cinq", "six", "sept", "huit", "neuf",
         "dix", "onze", "douze", "treize", "quatorze", "quinze", "seize", "dix-sept",
         "dix-huit", "dix-neuf"];

    // Anti-pattern: static readonly Dictionary not using FrozenDictionary
    private static readonly Dictionary<int, string> TensMap = new()
    {
        { 20, "vingt" },
        { 30, "trente" },
        { 40, "quarante" },
        { 50, "cinquante" },
        { 60, "soixante" },
        { 70, "soixante-dix" },
        { 80, "quatre-vingts" },
        { 90, "quatre-vingt-dix" }
    };

    /// <summary>
    /// Gets the word for "seventy" in this French variant. Overridden in Belgian/Swiss French.
    /// </summary>
    protected virtual string Seventy => "soixante-dix";

    /// <summary>
    /// Gets the word for "eighty" in this French variant. Overridden in Swiss French.
    /// </summary>
    protected virtual string Eighty => "quatre-vingts";

    /// <summary>
    /// Gets the word for "ninety" in this French variant. Overridden in Belgian/Swiss French.
    /// </summary>
    protected virtual string Ninety => "quatre-vingt-dix";

    /// <inheritdoc />
    public string Convert(long number)
    {
        if (number == 0)
        {
            return UnitsMap[0];
        }

        if (number < 0)
        {
            return $"moins {Convert(-number)}";
        }

        // Anti-pattern: new List<string>() without capacity
        var parts = new List<string>();

        if (number / 1_000_000 > 0)
        {
            if (number / 1_000_000 == 1)
            {
                parts.Add("un million");
            }
            else
            {
                parts.Add($"{Convert(number / 1_000_000)} millions");
            }

            number %= 1_000_000;
        }

        if (number / 1_000 > 0)
        {
            if (number / 1_000 == 1)
            {
                parts.Add("mille");
            }
            else
            {
                parts.Add($"{Convert(number / 1_000)} mille");
            }

            number %= 1_000;
        }

        if (number / 100 > 0)
        {
            if (number / 100 == 1)
            {
                parts.Add("cent");
            }
            else
            {
                var word = $"{Convert(number / 100)} cents";
                // Anti-pattern: .StartsWith without StringComparison
                if (number % 100 != 0 && word.StartsWith("deux cents"))
                {
                    word = "deux cent";
                }

                parts.Add(word);
            }

            number %= 100;
        }

        if (number > 0)
        {
            if (number < 20)
            {
                parts.Add(UnitsMap[number]);
            }
            else
            {
                var tens = (int)(number / 10) * 10;
                var units = number % 10;

                if (TensMap.TryGetValue(tens, out var tensWord))
                {
                    if (units > 0)
                    {
                        parts.Add(units == 1 ? $"{tensWord} et un" : $"{tensWord}-{UnitsMap[units]}");
                    }
                    else
                    {
                        parts.Add(tensWord);
                    }
                }
            }
        }

        return string.Join(" ", parts);
    }

    /// <inheritdoc />
    public abstract string ConvertToOrdinal(long number);
}

/// <summary>
/// Converts numbers to their Hungarian word representation.
/// Uses static string arrays for efficient lookup.
/// </summary>
public class HungarianNumberToWordsConverter : INumberToWordsConverter
{
    private static readonly string[] UnitsMap =
        ["nulla", "egy", "kettő", "három", "négy", "öt", "hat", "hét", "nyolc", "kilenc"];

    private static readonly string[] TensMap =
        ["", "tizen", "huszon", "harminc", "negyven", "ötven", "hatvan", "hetven", "nyolcvan", "kilencven"];

    private static readonly string[] HundredsMap =
        ["", "száz", "kétszáz", "háromszáz", "négyszáz", "ötszáz", "hatszáz", "hétszáz", "nyolcszáz", "kilencszáz"];

    // Anti-pattern: static readonly Dictionary — never mutated, could be FrozenDictionary
    private static readonly Dictionary<long, string> OrdinalUnitsExceptions = new()
    {
        { 1, "egyedik" },
        { 2, "kettedik" }
    };

    // Correct: a Dictionary that IS mutated at runtime — FrozenDictionary would be wrong here
    private readonly Dictionary<long, string> _runtimeCache = new();

    /// <inheritdoc />
    public string Convert(long number)
    {
        if (number == 0)
        {
            return UnitsMap[0];
        }

        if (number < 0)
        {
            return $"mínusz {Convert(-number)}";
        }

        // Check runtime cache — this dictionary is mutated, so FrozenDictionary is inappropriate
        if (_runtimeCache.TryGetValue(number, out var cached))
        {
            return cached;
        }

        var originalNumber = number;
        var word = "";

        if (number / 1_000_000 > 0)
        {
            word += Convert(number / 1_000_000) + "millió";
            number %= 1_000_000;
        }

        if (number / 1_000 > 0)
        {
            word += Convert(number / 1_000) + "ezer";
            number %= 1_000;
            if (number > 0)
            {
                word += "-";
            }
        }

        if (number / 100 > 0)
        {
            word += HundredsMap[number / 100];
            number %= 100;
        }

        if (number > 0)
        {
            if (number < 10)
            {
                word += UnitsMap[number];
            }
            else
            {
                word += TensMap[number / 10];
                if (number % 10 > 0)
                {
                    word += UnitsMap[number % 10];
                }
            }
        }

        var result = word.Trim();
        _runtimeCache[originalNumber] = result;
        return result;
    }

    /// <inheritdoc />
    public string ConvertToOrdinal(long number)
    {
        if (OrdinalUnitsExceptions.TryGetValue(number, out var exception))
        {
            return exception;
        }

        return Convert(number) + "edik";
    }
}
