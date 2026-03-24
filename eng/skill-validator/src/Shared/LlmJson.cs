using System.Text.Json;
using System.Text.RegularExpressions;

namespace SkillValidator.Shared;

/// <summary>
/// Shared utilities for parsing JSON from LLM responses.
/// LLMs often wrap JSON in markdown code blocks or produce invalid escape
/// sequences. These helpers handle both cases robustly.
/// </summary>
public static partial class LlmJson
{
    /// <summary>
    /// Extract a JSON string from LLM response text.
    /// Tries markdown code block first, then falls back to brace-matching.
    /// When brace-matching, validates candidates with JsonDocument.Parse and skips
    /// non-JSON brace groups (e.g., C# code snippets).
    /// </summary>
    public static string? ExtractJson(string content)
    {
        var codeBlockMatch = CodeBlockRegex().Match(content);
        if (codeBlockMatch.Success)
            return codeBlockMatch.Groups[1].Value;

        // Try each top-level brace group until we find valid JSON
        int searchFrom = 0;
        while (searchFrom < content.Length)
        {
            var candidate = ExtractOutermostJson(content, searchFrom);
            if (candidate is null) return null;

            if (TryParseJson(candidate.Text))
                return candidate.Text;

            // Also try with escape sanitization before giving up on this candidate
            var sanitized = InvalidEscapeRegex().Replace(candidate.Text, "");
            if (TryParseJson(sanitized))
                return candidate.Text;

            searchFrom = candidate.EndIndex + 1;
        }

        return null;
    }

    /// <summary>
    /// Parse a JSON string, tolerating invalid escape sequences that LLMs
    /// sometimes produce (e.g., \' or \a).
    /// </summary>
    public static JsonElement ParseLlmJson(string jsonStr, string context)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonStr);
            return doc.RootElement.Clone();
        }
        catch (JsonException originalError)
        {
            bool hasInvalidEscapes = InvalidEscapeRegex().IsMatch(jsonStr);

            if (!hasInvalidEscapes)
            {
                var snippet = jsonStr[..Math.Min(200, jsonStr.Length)];
                throw new InvalidOperationException(
                    $"Failed to parse {context} JSON. Original error: {originalError.Message}. JSON snippet: {snippet}");
            }

            // LLMs sometimes produce invalid JSON escape sequences (e.g., \' or \a).
            // Retry after removing backslashes before non-JSON-escape characters.
            try
            {
                var sanitized = InvalidEscapeRegex().Replace(jsonStr, "");
                using var doc = JsonDocument.Parse(sanitized);
                return doc.RootElement.Clone();
            }
            catch (JsonException retryErr)
            {
                var snippet = jsonStr[..Math.Min(200, jsonStr.Length)];
                throw new InvalidOperationException(
                    $"Failed to parse {context} JSON even after sanitizing invalid escapes. " +
                    $"Original error: {originalError.Message}. Retry error: {retryErr.Message}. JSON snippet: {snippet}");
            }
        }
    }

    private static bool TryParseJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record JsonCandidate(string Text, int EndIndex);

    private static JsonCandidate? ExtractOutermostJson(string text, int fromIndex = 0)
    {
        int start = text.IndexOf('{', fromIndex);
        if (start == -1) return null;

        int depth = 0;
        bool inString = false;
        bool escape = false;

        for (int i = start; i < text.Length; i++)
        {
            char ch = text[i];
            if (escape) { escape = false; continue; }
            if (ch == '\\') { escape = true; continue; }
            if (ch == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (ch == '{') depth++;
            if (ch == '}')
            {
                depth--;
                if (depth == 0) return new JsonCandidate(text[start..(i + 1)], i);
            }
        }

        return null;
    }

    [GeneratedRegex(@"```(?:json)?\s*(\{[\s\S]*?\})\s*```")]
    private static partial Regex CodeBlockRegex();

    [GeneratedRegex(@"\\(?![""\\\/bfnrtu])")]
    private static partial Regex InvalidEscapeRegex();
}
