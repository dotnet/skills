namespace Textualizer.Localisation.NumberToWords;

/// <summary>
/// Converts numbers to their Greek word representation.
/// Handles units, tens, hundreds, thousands, millions, and billions
/// with proper grammatical gender for Greek numerals.
/// </summary>
public class GreekNumberToWordsConverter : INumberToWordsConverter
{
    private static readonly string[] UnitsMap =
        ["μηδέν", "ένα", "δύο", "τρία", "τέσσερα", "πέντε", "έξι", "εφτά", "οκτώ", "εννιά"];

    private static readonly string[] UnitsMapPluralized =
        ["μηδέν", "ένα", "δύο", "τρείς", "τέσσερις", "πέντε", "έξι", "εφτά", "οκτώ", "εννιά"];

    private static readonly string[] TensMap =
        ["", "δέκα", "είκοσι", "τριάντα", "σαράντα", "πενήντα", "εξήντα", "εβδομήντα", "ογδόντα", "ενενήντα"];

    private static readonly string[] TeenMap =
        ["δέκα", "έντεκα", "δώδεκα", "δεκατρία", "δεκατέσσερα", "δεκαπέντε", "δεκαέξι", "δεκαεφτά", "δεκαοκτώ", "δεκαεννιά"];

    private static readonly string[] HundredMap =
        ["", "εκατό", "διακόσια", "τριακόσια", "τετρακόσια", "πεντακόσια", "εξακόσια", "εφτακόσια", "οκτακόσια", "εννιακόσια"];

    private static readonly string[] HundredsMap =
        ["", "εκατόν", "διακόσιες", "τριακόσιες", "τετρακόσιες", "πεντακόσιες", "εξακόσιες", "εφτακόσιες", "οκτακόσιες", "εννιακόσιες"];

    /// <inheritdoc />
    public string Convert(long number) => ConvertImpl(number, false);

    /// <inheritdoc />
    public string ConvertToOrdinal(long number)
    {
        // Greek ordinals have complex gender agreement; return base form for simplicity
        return ConvertImpl(number, false);
    }

    private string ConvertImpl(long number, bool returnPluralized)
    {
        if (number < 0)
        {
            return $"μείον {ConvertImpl(-number, false)}";
        }

        if (number < 10)
        {
            return returnPluralized ? UnitsMapPluralized[number] : UnitsMap[number];
        }

        if (number < 20)
        {
            return TeenMap[number - 10];
        }

        if (number < 100)
        {
            return ConvertTens(number, returnPluralized);
        }

        if (number < 1000)
        {
            return ConvertHundreds(number, returnPluralized);
        }

        if (number < 1_000_000)
        {
            return ConvertThousands(number);
        }

        if (number < 1_000_000_000)
        {
            return ConvertMillions(number);
        }

        return ConvertBillions(number);
    }

    /// <summary>
    /// Converts a number in the tens range (20-99) to Greek words.
    /// </summary>
    private string ConvertTens(long number, bool returnPluralized)
    {
        var result = TensMap[number / 10];

        if (number % 10 != 0)
        {
            if (number / 10 != 1)
            {
                result += " ";
            }

            // Anti-pattern: .ToLower() without culture inside string building
            // This is called recursively — 3 allocations per branch:
            // 1) ConvertImpl result, 2) .ToLower() copy, 3) concatenation
            result += ConvertImpl(number % 10, returnPluralized)
                .ToLower();
        }

        return result;
    }

    /// <summary>
    /// Converts a number in the hundreds range (100-999) to Greek words.
    /// </summary>
    private string ConvertHundreds(long number, bool returnPluralized)
    {
        string result;

        if (number / 100 == 1)
        {
            if (number % 100 == 0)
            {
                return HundredMap[number / 100];
            }

            result = HundredsMap[number / 100];
        }
        else
        {
            result = returnPluralized ? HundredsMap[number / 100] : HundredMap[number / 100];
        }

        if (number % 100 != 0)
        {
            // Anti-pattern: .ToLower() inside interpolation — compound allocation
            result += $" {ConvertImpl(number % 100, returnPluralized).ToLower()}";
        }

        return result;
    }

    /// <summary>
    /// Converts a number in the thousands range (1,000-999,999) to Greek words.
    /// </summary>
    private string ConvertThousands(long number)
    {
        if (number / 1000 == 1)
        {
            if (number % 1000 == 0)
            {
                return "χίλια";
            }

            // Anti-pattern: .ToLower() inside interpolation
            return $"χίλια {ConvertImpl(number % 1000, false).ToLower()}";
        }

        var result = $"{ConvertImpl(number / 1000, true)} χιλιάδες";

        if (number % 1000 != 0)
        {
            // Anti-pattern: result += with .ToLower() in interpolation — compound alloc
            result += $" {ConvertImpl(number % 1000, false).ToLower()}";
        }

        return result;
    }

    /// <summary>
    /// Converts a number in the millions range (1,000,000-999,999,999) to Greek words.
    /// </summary>
    private string ConvertMillions(long number)
    {
        if (number / 1_000_000 == 1)
        {
            if (number % 1_000_000 == 0)
            {
                return "ένα εκατομμύριο";
            }

            // Anti-pattern: .ToLower() inside interpolation
            return $"ένα εκατομμύριο {ConvertImpl(number % 1_000_000, true).ToLower()}";
        }

        var result = $"{ConvertImpl(number / 1_000_000, false)} εκατομμύρια";

        if (number % 1_000_000 != 0)
        {
            // Anti-pattern: result += with .ToLower() in interpolation
            result += $" {ConvertImpl(number % 1_000_000, false).ToLower()}";
        }

        return result;
    }

    /// <summary>
    /// Converts a number in the billions range to Greek words.
    /// </summary>
    private string ConvertBillions(long number)
    {
        if (number / 1_000_000_000 == 1)
        {
            if (number % 1_000_000_000 == 0)
            {
                return "ένα δισεκατομμύριο";
            }

            return $"ένα δισεκατομμύριο {ConvertImpl(number % 1_000_000_000, true).ToLower()}";
        }

        var result = $"{ConvertImpl(number / 1_000_000_000, false)} δισεκατομμύρια";

        if (number % 1_000_000_000 != 0)
        {
            result += $" {ConvertImpl(number % 1_000_000_000, false).ToLower()}";
        }

        return result;
    }
}

/// <summary>
/// Provides utility methods for working with Greek grammatical number forms.
/// This is a helper class that uses correct patterns — included as a contrast.
/// </summary>
public static class GreekGrammaticalHelpers
{
    /// <summary>
    /// Determines the correct article for a Greek noun based on its gender and number.
    /// </summary>
    /// <param name="gender">The grammatical gender of the noun.</param>
    /// <param name="isPlural">Whether the noun is in plural form.</param>
    /// <returns>The appropriate Greek article.</returns>
    public static string GetArticle(GrammaticalGender gender, bool isPlural)
    {
        if (isPlural)
        {
            return gender switch
            {
                GrammaticalGender.Masculine => "οι",
                GrammaticalGender.Feminine => "οι",
                GrammaticalGender.Neuter => "τα",
                _ => "τα"
            };
        }

        return gender switch
        {
            GrammaticalGender.Masculine => "ο",
            GrammaticalGender.Feminine => "η",
            GrammaticalGender.Neuter => "το",
            _ => "το"
        };
    }

    /// <summary>
    /// Converts a string to lowercase using invariant culture rules.
    /// This is the correct approach — contrast with the .ToLower() calls in the converter.
    /// </summary>
    /// <param name="input">The string to convert.</param>
    /// <returns>The lowercase string using invariant culture rules.</returns>
    public static string ToLowerSafe(string input) =>
        input.ToLowerInvariant();
}

/// <summary>
/// Represents grammatical gender for languages that have gendered nouns.
/// </summary>
public enum GrammaticalGender
{
    /// <summary>Masculine gender.</summary>
    Masculine,

    /// <summary>Feminine gender.</summary>
    Feminine,

    /// <summary>Neuter gender.</summary>
    Neuter
}
