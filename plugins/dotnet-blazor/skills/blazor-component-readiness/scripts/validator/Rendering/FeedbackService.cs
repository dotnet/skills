using System.Text;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Rendering;

public static class FeedbackService
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static AssessmentFeedback Parse(
        ReadOnlyMemory<byte> bytes,
        ReadinessAssessment assessment,
        IReadOnlyList<string>? associatedRequirementIds = null)
    {
        BoundedIO.EnsureLength(bytes.Length, ResourceLimits.SerializedArtifactBytes, "assessment feedback");
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes.Span);
        }
        catch (DecoderFallbackException exception)
        {
            throw new DeterministicValidationException(
                $"Assessment feedback must be valid UTF-8: {exception.Message}");
        }

        var lines = text.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].EndsWith('\r'))
            {
                lines[index] = lines[index][..^1];
            }
        }

        if (lines.Length < 4 ||
            lines[0] != "# Assessment feedback" ||
            lines[1].Length != 0 ||
            lines[2] != "| Requirement IDs | Feedback |" ||
            lines[3] != "|---|---|" ||
            lines.Skip(4).Any(string.IsNullOrEmpty))
        {
            throw new DeterministicValidationException(
                "Feedback must contain only '# Assessment feedback' and the two-column Requirement IDs/Feedback table.");
        }

        var rubric = RubricLoader.Load(assessment.RubricVersion);
        var knownIds = rubric.CoreRequirements.Select(row => row.Id)
            .Concat(rubric.Overlays.SelectMany(overlay => overlay.Requirements).Select(row => row.Id))
            .ToHashSet(StringComparer.Ordinal);
        if (associatedRequirementIds is not null &&
            assessment.AssessmentKind != "component")
        {
            throw new DeterministicValidationException(
                "Only component feedback may reference requirements from an associated package assessment.");
        }

        var selectedIds = assessment.SelectedIds
            .Concat(associatedRequirementIds ?? [])
            .ToHashSet(StringComparer.Ordinal);
        var entries = new List<AssessmentFeedbackEntry>();
        foreach (var line in lines.Skip(4))
        {
            var (key, payload) = ParseRow(line);
            var ids = ParseKey(key);
            var unknown = ids.FirstOrDefault(id => !knownIds.Contains(id));
            if (unknown is not null)
            {
                throw new DeterministicValidationException(
                    $"Feedback references unknown requirement ID '{unknown}'.");
            }

            var orphaned = ids.FirstOrDefault(id => !selectedIds.Contains(id));
            if (orphaned is not null)
            {
                throw new DeterministicValidationException(
                    $"Feedback requirement ID '{orphaned}' is orphaned from this assessment.");
            }

            var set = ids.ToHashSet(StringComparer.Ordinal);
            foreach (var existing in entries)
            {
                var existingSet = existing.RequirementIds.ToHashSet(StringComparer.Ordinal);
                if (set.SetEquals(existingSet))
                {
                    throw new DeterministicValidationException(
                        "Feedback contains a duplicate normalized requirement-ID set.");
                }

                if (set.Overlaps(existingSet))
                {
                    throw new DeterministicValidationException(
                        "Feedback contains overlapping or ambiguous requirement-ID sets.");
                }
            }

            entries.Add(new AssessmentFeedbackEntry(ids, payload));
        }

        return new AssessmentFeedback(entries, ContractJson.RawDigest(bytes.Span));
    }

    private static (string Key, string Payload) ParseRow(string line)
    {
        if (line.Length < 3 || line[0] != '|' || line[^1] != '|')
        {
            throw new DeterministicValidationException(
                "Each feedback row must be a two-column Markdown table row.");
        }

        var delimiters = new List<int>();
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] != '|' || IsEscaped(line, index))
            {
                continue;
            }

            delimiters.Add(index);
        }

        if (delimiters.Count != 3 || delimiters[0] != 0 || delimiters[^1] != line.Length - 1)
        {
            throw new DeterministicValidationException(
                "Feedback rows require exactly two columns; literal pipes must remain escaped.");
        }

        return (
            line[(delimiters[0] + 1)..delimiters[1]],
            line[(delimiters[1] + 1)..delimiters[2]]);
    }

    private static bool IsEscaped(string line, int index)
    {
        var backslashes = 0;
        for (var current = index - 1; current >= 0 && line[current] == '\\'; current--)
        {
            backslashes++;
        }

        return backslashes % 2 == 1;
    }

    private static IReadOnlyList<string> ParseKey(string key)
    {
        var tokens = key.Split(',', StringSplitOptions.None);
        if (tokens.Length == 0)
        {
            throw new DeterministicValidationException(
                "Feedback requirement-ID sets cannot be empty.");
        }

        var ids = new List<string>();
        foreach (var token in tokens)
        {
            var trimmed = token.Trim();
            if (trimmed.Length < 3 || trimmed[0] != '`' || trimmed[^1] != '`')
            {
                throw new DeterministicValidationException(
                    "Feedback requirement IDs must be comma-separated canonical IDs enclosed in backticks.");
            }

            var id = ContractJson.NormalizeText(
                trimmed[1..^1],
                "feedback requirement ID",
                64);
            ids.Add(id);
        }

        var canonical = ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (canonical.Length != ids.Count)
        {
            throw new DeterministicValidationException(
                "Feedback requirement-ID sets cannot contain duplicate IDs.");
        }

        return canonical;
    }
}
