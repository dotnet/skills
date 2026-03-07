using System.Text.RegularExpressions;
using SkillValidator.Models;

namespace SkillValidator.Services;

public sealed record SkillProfile(
    string Name,
    int TokenCount,
    string ComplexityTier, // "compact" | "detailed" | "standard" | "comprehensive"
    int SectionCount,
    int CodeBlockCount,
    int NumberedStepCount,
    int BulletCount,
    bool HasFrontmatter,
    bool HasWhenToUse,
    bool HasWhenNotToUse,
    int ResourceFileCount,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public static partial class SkillProfiler
{
    // Thresholds grounded in SkillsBench paper data
    private const int TokenSweetLow = 200;
    private const int TokenSweetHigh = 2500;
    private const int TokenWarnHigh = 5000;
    internal const int MaxDescriptionLength = 1024;
    internal const int MaxAggregateDescriptionLength = 15_000;
    private const int MaxNameLength = 64;
    private const int MaxCompatibilityLength = 500;
    private const int MaxBodyLines = 500;

    public static SkillProfile AnalyzeSkill(SkillInfo skill)
    {
        var content = skill.SkillMdContent;
        int tokenCount = (int)Math.Ceiling(content.Length / 4.0);

        bool hasFrontmatter = FrontmatterRegex().IsMatch(content);

        // Strip frontmatter for structural analysis
        var body = FrontmatterStripRegex().Replace(content, "");

        int sectionCount = SectionRegex().Matches(body).Count;
        int codeBlockCount = CodeBlockRegex().Matches(body).Count / 2;
        int numberedStepCount = NumberedStepRegex().Matches(body).Count;
        int bulletCount = BulletRegex().Matches(body).Count;

        bool hasWhenToUse = WhenToUseRegex().IsMatch(body);
        bool hasWhenNotToUse = WhenNotToUseRegex().IsMatch(body);

        string complexityTier = tokenCount switch
        {
            < 400 => "compact",
            <= 2500 => "detailed",
            <= 5000 => "standard",
            _ => "comprehensive",
        };

        int resourceFileCount = skill.EvalConfig?.Scenarios
            .Sum(s => s.Setup?.Files?.Count ?? 0) ?? 0;

        var errors = new List<string>();
        var warnings = new List<string>();

        // --- agentskills.io spec: name validation ---
        // https://agentskills.io/specification#name-field
        // Spec uses "Must" for all name constraints — violations are errors.
        ValidateName(skill.Name, Path.GetFileName(skill.Path), errors);

        // --- agentskills.io spec: description validation ---
        // https://agentskills.io/specification#description-field
        // "Must be 1-1024 characters"
        if (skill.Description.Length > MaxDescriptionLength)
        {
            errors.Add($"Skill description is {skill.Description.Length:N0} characters — maximum is {MaxDescriptionLength:N0}. Shorten the description in SKILL.md frontmatter.");
        }
        else if (skill.Description.Length == 0 && hasFrontmatter)
        {
            errors.Add("YAML frontmatter has no description — required by spec. Agents use description for skill discovery.");
        }

        // --- agentskills.io spec: compatibility field ---
        // https://agentskills.io/specification#compatibility-field
        // "Must be 1-500 characters if provided"
        if (skill.Compatibility is { Length: > MaxCompatibilityLength })
        {
            errors.Add($"Compatibility field is {skill.Compatibility.Length} characters — maximum is {MaxCompatibilityLength}.");
        }
        else if (skill.Compatibility is not null && string.IsNullOrWhiteSpace(skill.Compatibility))
        {
            errors.Add("Compatibility field must be 1-500 non-whitespace characters when provided.");
        }

        // --- agentskills.io spec: body line count ---
        var trimmedBody = body.TrimEnd('\r', '\n');
        int bodyLineCount = trimmedBody.Length == 0 ? 0 : trimmedBody.Split('\n').Length;
        if (bodyLineCount > MaxBodyLines)
        {
            errors.Add($"SKILL.md body is {bodyLineCount} lines — maximum is {MaxBodyLines}. Move detailed reference material to separate files.");
        }

        // --- agentskills.io spec: file reference depth ---
        foreach (Match refMatch in FileRefRegex().Matches(body))
        {
            var refPath = refMatch.Groups[1].Value;
            if (refPath.StartsWith("http", StringComparison.OrdinalIgnoreCase) || refPath.StartsWith('#'))
                continue;

            // Strip fragment anchors (e.g. "file.md#section")
            int fragmentIndex = refPath.IndexOf('#');
            if (fragmentIndex >= 0)
                refPath = refPath[..fragmentIndex];
            if (refPath.Length == 0)
                continue;

            // Normalize: trim leading "./"
            if (refPath.StartsWith("./"))
                refPath = refPath[2..];

            var segments = refPath.Split('/');

            // Reject parent-directory traversals
            if (segments.Any(s => s == ".."))
            {
                errors.Add($"File reference '{refMatch.Groups[1].Value}' uses parent-directory traversal — references must stay within the skill directory.");
                continue;
            }

            // Depth = directory segments only (exclude filename)
            int dirDepth = segments.Length - 1;
            if (dirDepth > 1) // e.g. "references/deep/file.md" = dirDepth 2
            {
                errors.Add($"File reference '{refMatch.Groups[1].Value}' is {dirDepth} directories deep — maximum is 1 level from SKILL.md.");
            }
        }

        // --- Token size warnings ---
        if (tokenCount > TokenWarnHigh)
        {
            warnings.Add(
                $"Skill is {tokenCount:N0} tokens — \"comprehensive\" skills hurt performance by 2.9pp on average. Consider splitting into 2–3 focused skills.");
        }
        else if (tokenCount > TokenSweetHigh)
        {
            warnings.Add(
                $"Skill is {tokenCount:N0} tokens — approaching \"comprehensive\" range where gains diminish.");
        }
        else if (tokenCount < TokenSweetLow)
        {
            warnings.Add(
                $"Skill is only {tokenCount} tokens — may be too sparse to provide actionable guidance.");
        }

        if (sectionCount == 0)
            warnings.Add("No section headers — agents navigate structured documents better.");

        if (codeBlockCount == 0)
            warnings.Add("No code blocks — agents perform better with concrete snippets and commands.");

        if (numberedStepCount == 0)
            warnings.Add("No numbered workflow steps — agents follow sequenced procedures more reliably.");

        if (!hasFrontmatter)
            warnings.Add("No YAML frontmatter — agents use name/description for skill discovery.");

        // Check if eval prompts explicitly reference the skill by name — this biases
        // baseline runs (agent wastes time searching) and forces activation instead of
        // testing organic discovery.
        if (skill.EvalConfig is not null && !string.IsNullOrWhiteSpace(skill.Name))
        {
            foreach (var scenario in skill.EvalConfig.Scenarios)
            {
                if (scenario.Prompt.Contains(skill.Name, StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"Eval scenario '{scenario.Name}' prompt mentions skill name '{skill.Name}' — this biases baseline runs and forces activation.");
            }
        }

        return new SkillProfile(
            Name: skill.Name,
            TokenCount: tokenCount,
            ComplexityTier: complexityTier,
            SectionCount: sectionCount,
            CodeBlockCount: codeBlockCount,
            NumberedStepCount: numberedStepCount,
            BulletCount: bulletCount,
            HasFrontmatter: hasFrontmatter,
            HasWhenToUse: hasWhenToUse,
            HasWhenNotToUse: hasWhenNotToUse,
            ResourceFileCount: resourceFileCount,
            Errors: errors,
            Warnings: warnings);
    }

    /// <summary>
    /// Validate a skill name against the agentskills.io spec.
    /// https://agentskills.io/specification#name-field
    /// All constraints use "Must" in the spec, so violations are errors.
    /// </summary>
    internal static void ValidateName(string name, string directoryName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("Skill name is empty — must be 1-64 lowercase alphanumeric characters and hyphens.");
            return;
        }

        if (name.Length > MaxNameLength)
            errors.Add($"Skill name '{name}' is {name.Length} characters — maximum is {MaxNameLength}.");

        if (!NameFormatRegex().IsMatch(name))
            errors.Add($"Skill name '{name}' contains invalid characters — must be lowercase alphanumeric and hyphens only.");

        if (name.StartsWith('-') || name.EndsWith('-'))
            errors.Add($"Skill name '{name}' starts or ends with a hyphen.");

        if (name.Contains("--"))
            errors.Add($"Skill name '{name}' contains consecutive hyphens.");

        if (!string.Equals(name, directoryName, StringComparison.Ordinal))
            errors.Add($"Skill name '{name}' does not match directory name '{directoryName}'.");
    }

    public static string FormatProfileLine(SkillProfile profile)
    {
        var tierIndicator = profile.ComplexityTier switch
        {
            "detailed" or "compact" => "✓",
            "comprehensive" => "✗",
            _ => "~",
        };

        return
            $"📊 {profile.Name}: {profile.TokenCount:N0} tokens ({profile.ComplexityTier} {tierIndicator}), " +
            $"{profile.SectionCount} sections, {profile.CodeBlockCount} code blocks";
    }

    public static IReadOnlyList<string> FormatProfileWarnings(SkillProfile profile) =>
        profile.Warnings.Select(w => $"   ⚠  {w}").ToList();

    public static IReadOnlyList<string> FormatDiagnosisHints(SkillProfile profile)
    {
        if (profile.Warnings.Count == 0) return [];
        return ["Possible causes from skill analysis:",
            ..profile.Warnings.Select(w => $"  • {w}")];
    }

    [GeneratedRegex(@"^---\r?\n[\s\S]*?\r?\n---", RegexOptions.Multiline)]
    private static partial Regex FrontmatterRegex();

    [GeneratedRegex(@"^---\r?\n[\s\S]*?\r?\n---\r?\n?")]
    private static partial Regex FrontmatterStripRegex();

    [GeneratedRegex(@"^#{1,4}\s+", RegexOptions.Multiline)]
    private static partial Regex SectionRegex();

    [GeneratedRegex(@"```")]
    private static partial Regex CodeBlockRegex();

    [GeneratedRegex(@"^\d+\.\s", RegexOptions.Multiline)]
    private static partial Regex NumberedStepRegex();

    [GeneratedRegex(@"^[-*]\s", RegexOptions.Multiline)]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^#{1,4}\s+when\s+to\s+use", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex WhenToUseRegex();

    [GeneratedRegex(@"^#{1,4}\s+when\s+not\s+to\s+use", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex WhenNotToUseRegex();

    [GeneratedRegex(@"^[a-z0-9-]+$")]
    private static partial Regex NameFormatRegex();

    [GeneratedRegex(@"\]\(([^)]+)\)")]
    private static partial Regex FileRefRegex();
}
