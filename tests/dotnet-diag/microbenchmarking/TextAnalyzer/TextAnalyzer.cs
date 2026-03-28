using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TextAnalysis;

/// <summary>
/// Analyzes text content and provides statistical summaries.
/// Pre-computes word metrics on construction for efficient repeated queries.
/// </summary>
public class TextAnalyzer
{
    private readonly string[] _words;
    private readonly ReadOnlyCollection<int> _wordLengths;

    public TextAnalyzer(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        int[] lengths = new int[_words.Length];
        for (int i = 0; i < _words.Length; i++)
            lengths[i] = _words[i].Length;
        _wordLengths = new ReadOnlyCollection<int>(lengths);
    }

    /// <summary>Gets the number of words in the text.</summary>
    public int WordCount => _words.Length;

    /// <summary>Computes the average word length.</summary>
    public double AverageWordLength()
    {
        if (_wordLengths.Count == 0) return 0;
        int total = 0;
        for (int i = 0; i < _wordLengths.Count; i++)
            total += _wordLengths[i];
        return (double)total / _wordLengths.Count;
    }

    /// <summary>
    /// Builds a histogram of word lengths. Index i contains the count
    /// of words with length i.
    /// </summary>
    public ReadOnlyCollection<int> WordLengthHistogram()
    {
        int max = 0;
        for (int i = 0; i < _wordLengths.Count; i++)
            if (_wordLengths[i] > max) max = _wordLengths[i];
        if (max == 0) return new ReadOnlyCollection<int>([]);
        int[] histogram = new int[max + 1];
        for (int i = 0; i < _wordLengths.Count; i++)
            histogram[_wordLengths[i]]++;
        return new ReadOnlyCollection<int>(histogram);
    }

    /// <summary>Counts the number of unique words (case-insensitive).</summary>
    public int UniqueWordCount()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string word in _words)
            seen.Add(word);
        return seen.Count;
    }
}
