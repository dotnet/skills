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
    IReadOnlyList<string> Warnings);

public static partial class SkillProfiler
{
    // Thresholds grounded in SkillsBench paper data
    private const int TokenSweetLow = 200;
    private const int TokenSweetHigh = 2500;
    private const int TokenWarnHigh = 5000;

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

        var warnings = new List<string>();

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
            Warnings: warnings);
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
}
