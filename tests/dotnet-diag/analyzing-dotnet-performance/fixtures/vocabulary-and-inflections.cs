using System.Text.RegularExpressions;

namespace Textualizer.Inflections;

/// <summary>
/// Manages a set of pluralization and singularization rules, along with uncountable
/// and irregular word lists, for transforming English words between their plural
/// and singular forms.
/// </summary>
public class WordVocabulary
{
    private readonly List<InflectionEntry> _plurals = [];
    private readonly List<InflectionEntry> _singulars = [];

    // Anti-pattern: StringComparer.CurrentCultureIgnoreCase is ~3.3x slower than
    // OrdinalIgnoreCase for this use case (English uncountable words are ASCII)
    private readonly HashSet<string> _uncountables = new(StringComparer.CurrentCultureIgnoreCase);

    // Correct pattern: uses OrdinalIgnoreCase for the irregulars map
    private readonly Dictionary<string, string> _irregularPlurals =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _irregularSingulars =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the number of pluralization rules currently registered.
    /// </summary>
    public int PluralRuleCount => _plurals.Count;

    /// <summary>
    /// Gets the number of singularization rules currently registered.
    /// </summary>
    public int SingularRuleCount => _singulars.Count;

    /// <summary>
    /// Gets the number of uncountable words registered.
    /// </summary>
    public int UncountableCount => _uncountables.Count;

    /// <summary>
    /// Adds a pluralization rule with the given regex pattern and replacement.
    /// </summary>
    /// <param name="pattern">A regex pattern that matches the singular form.</param>
    /// <param name="replacement">The replacement string for generating the plural form.</param>
    public void AddPlural(string pattern, string replacement)
    {
        _uncountables.Remove(pattern);
        _uncountables.Remove(replacement);
        // Anti-pattern: Each AddPlural creates a compiled regex via InflectionEntry
        _plurals.Add(new InflectionEntry(pattern, replacement));
    }

    /// <summary>
    /// Adds a singularization rule with the given regex pattern and replacement.
    /// </summary>
    /// <param name="pattern">A regex pattern that matches the plural form.</param>
    /// <param name="replacement">The replacement string for generating the singular form.</param>
    public void AddSingular(string pattern, string replacement)
    {
        _uncountables.Remove(pattern);
        _uncountables.Remove(replacement);
        _singulars.Add(new InflectionEntry(pattern, replacement));
    }

    /// <summary>
    /// Registers an irregular plural/singular pair (e.g., "person" / "people").
    /// </summary>
    /// <param name="singular">The singular form of the word.</param>
    /// <param name="plural">The plural form of the word.</param>
    public void AddIrregular(string singular, string plural)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(singular);
        ArgumentException.ThrowIfNullOrWhiteSpace(plural);

        _uncountables.Remove(singular);
        _uncountables.Remove(plural);
        _irregularPlurals[singular] = plural;
        _irregularSingulars[plural] = singular;
    }

    /// <summary>
    /// Marks a word as uncountable (e.g., "equipment", "information", "rice").
    /// Uncountable words are returned unchanged by both pluralization and singularization.
    /// </summary>
    /// <param name="word">The uncountable word to register.</param>
    public void AddUncountable(string word)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        _uncountables.Add(word.Trim());
    }

    /// <summary>
    /// Returns the plural form of the given word using registered rules.
    /// </summary>
    /// <param name="word">The word to pluralize.</param>
    /// <returns>The plural form of the word.</returns>
    public string Pluralize(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return word;
        }

        if (_uncountables.Contains(word))
        {
            return word;
        }

        if (_irregularPlurals.TryGetValue(word, out var irregularPlural))
        {
            return irregularPlural;
        }

        return ApplyRules(_plurals, word) ?? word;
    }

    /// <summary>
    /// Returns the singular form of the given word using registered rules.
    /// </summary>
    /// <param name="word">The word to singularize.</param>
    /// <returns>The singular form of the word.</returns>
    public string Singularize(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return word;
        }

        if (_uncountables.Contains(word))
        {
            return word;
        }

        if (_irregularSingulars.TryGetValue(word, out var irregularSingular))
        {
            return irregularSingular;
        }

        return ApplyRules(_singulars, word) ?? word;
    }

    /// <summary>
    /// Checks whether a word is registered as uncountable.
    /// </summary>
    /// <param name="word">The word to check.</param>
    /// <returns>true if the word is uncountable; otherwise, false.</returns>
    public bool IsUncountable(string word) =>
        !string.IsNullOrWhiteSpace(word) && _uncountables.Contains(word.Trim());

    private static string? ApplyRules(List<InflectionEntry> rules, string word)
    {
        for (var i = rules.Count - 1; i >= 0; i--)
        {
            var result = rules[i].Apply(word);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>
    /// Internal rule entry holding a compiled regex and replacement pattern.
    /// </summary>
    private class InflectionEntry
    {
        private readonly Regex _regex;
        private readonly string _replacement;

        public InflectionEntry(string pattern, string replacement)
        {
            // Anti-pattern: compiled regex per rule — with 40+ rules this is
            // 40+ compiled regexes at startup, consuming 100-500ms
            _regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            _replacement = replacement;
        }

        public string? Apply(string word)
        {
            if (!_regex.IsMatch(word))
            {
                return null;
            }

            return _regex.Replace(word, _replacement);
        }
    }
}

/// <summary>
/// Provides default English inflection rules for pluralization and singularization.
/// </summary>
public static class DefaultInflections
{
    /// <summary>
    /// Loads the default English inflection rules into the given vocabulary.
    /// These rules cover common English pluralization patterns including
    /// regular plurals, -ies/-ves transformations, and Latin/Greek-origin words.
    /// </summary>
    /// <param name="vocabulary">The vocabulary to populate with default rules.</param>
    public static void Load(WordVocabulary vocabulary)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);

        // Each AddPlural/AddSingular creates a compiled regex via InflectionEntry
        vocabulary.AddPlural("$", "s");
        vocabulary.AddPlural("s$", "s");
        vocabulary.AddPlural("^(ax|test)is$", "$1es");
        vocabulary.AddPlural("(octop|vir)us$", "$1i");
        vocabulary.AddPlural("(alias|status)$", "$1es");
        vocabulary.AddPlural("(bu|mis|gas)s$", "$1ses");
        vocabulary.AddPlural("(buffal|tomat)o$", "$1oes");
        vocabulary.AddPlural("([ti])um$", "$1a");
        vocabulary.AddPlural("([ti])a$", "$1a");
        vocabulary.AddPlural("sis$", "ses");
        vocabulary.AddPlural("(?:([^f])fe|([lr])f)$", "$1$2ves");
        vocabulary.AddPlural("(hive)$", "$1s");
        vocabulary.AddPlural("([^aeiouy]|qu)y$", "$1ies");
        vocabulary.AddPlural("(x|ch|ss|sh)$", "$1es");
        vocabulary.AddPlural("(matr|vert|append)ix|ex$", "$1ices");
        vocabulary.AddPlural("([m|l])ouse$", "$1ice");
        vocabulary.AddPlural("^(ox)$", "$1en");
        vocabulary.AddPlural("(quiz)$", "$1zes");

        vocabulary.AddSingular("s$", "");
        vocabulary.AddSingular("(ss)$", "$1");
        vocabulary.AddSingular("(n)ews$", "$1ews");
        vocabulary.AddSingular("([ti])a$", "$1um");
        vocabulary.AddSingular("((a)naly|(b)a|(d)iagno|(p)arenthe|(p)rogno|(s)ynop|(t)he)(sis|ses)$", "$1sis");
        vocabulary.AddSingular("(^analy)(sis|ses)$", "$1sis");
        vocabulary.AddSingular("([^f])ves$", "$1fe");
        vocabulary.AddSingular("(hive)s$", "$1");
        vocabulary.AddSingular("(tive)s$", "$1");
        vocabulary.AddSingular("([lr])ves$", "$1f");
        vocabulary.AddSingular("([^aeiouy]|qu)ies$", "$1y");
        vocabulary.AddSingular("(s)eries$", "$1eries");
        vocabulary.AddSingular("(m)ovies$", "$1ovie");
        vocabulary.AddSingular("(x|ch|ss|sh)es$", "$1");
        vocabulary.AddSingular("([m|l])ice$", "$1ouse");
        vocabulary.AddSingular("(bus)(es)?$", "$1");
        vocabulary.AddSingular("(o)es$", "$1");
        vocabulary.AddSingular("(shoe)s$", "$1");
        vocabulary.AddSingular("(cris|test)(is|es)$", "$1is");
        vocabulary.AddSingular("^(a)x[ie]s$", "$1xis");
        vocabulary.AddSingular("(octop|vir)(us|i)$", "$1us");
        vocabulary.AddSingular("(alias|status)(es)?$", "$1");
        vocabulary.AddSingular("^(ox)en", "$1");
        vocabulary.AddSingular("(vert|ind)ices$", "$1ex");
        vocabulary.AddSingular("(matr)ices$", "$1ix");
        vocabulary.AddSingular("(quiz)zes$", "$1");

        vocabulary.AddIrregular("person", "people");
        vocabulary.AddIrregular("man", "men");
        vocabulary.AddIrregular("child", "children");
        vocabulary.AddIrregular("sex", "sexes");
        vocabulary.AddIrregular("move", "moves");
        vocabulary.AddIrregular("goose", "geese");
        vocabulary.AddIrregular("alumnus", "alumni");

        vocabulary.AddUncountable("equipment");
        vocabulary.AddUncountable("information");
        vocabulary.AddUncountable("rice");
        vocabulary.AddUncountable("money");
        vocabulary.AddUncountable("species");
        vocabulary.AddUncountable("series");
        vocabulary.AddUncountable("fish");
        vocabulary.AddUncountable("sheep");
        vocabulary.AddUncountable("jeans");
        vocabulary.AddUncountable("police");
    }
}
