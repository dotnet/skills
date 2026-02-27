using SkillValidator.Models;
using SkillValidator.Services;

namespace SkillValidator.Tests;

public class AnalyzeSkillTests
{
    private static SkillInfo MakeSkill(string content, string name = "test-skill")
    {
        return new SkillInfo(
            Name: name,
            Description: "Test skill",
            Path: "/tmp/test-skill",
            SkillMdPath: "/tmp/test-skill/SKILL.md",
            SkillMdContent: content,
            EvalPath: null,
            EvalConfig: null);
    }

    [Fact]
    public void DetectsFrontmatter()
    {
        var skill = MakeSkill("---\nname: foo\n---\n# Hello\nSome content");
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.True(profile.HasFrontmatter);
    }

    [Fact]
    public void DetectsMissingFrontmatter()
    {
        var skill = MakeSkill("# Hello\nSome content");
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.False(profile.HasFrontmatter);
        Assert.Contains(profile.Warnings, w => w.Contains("frontmatter"));
    }

    [Fact]
    public void CountsSectionsAndCodeBlocks()
    {
        var content = string.Join("\n",
            "---\nname: foo\n---",
            "# Title",
            "## Section 1",
            "```bash\necho hello\n```",
            "## Section 2",
            "```python\nprint('hi')\n```",
            "```js\nconsole.log('x')\n```");
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.Equal(3, profile.SectionCount);
        Assert.Equal(3, profile.CodeBlockCount);
    }

    [Fact]
    public void CountsNumberedSteps()
    {
        var content = "---\nname: foo\n---\n# Steps\n1. First\n2. Second\n3. Third\n";
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.Equal(3, profile.NumberedStepCount);
    }

    [Fact]
    public void ClassifiesCompactSkills()
    {
        var content = "---\nname: foo\n---\n# Short\nBrief.";
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.Equal("compact", profile.ComplexityTier);
    }

    [Fact]
    public void ClassifiesComprehensiveSkillsAndWarns()
    {
        // >5000 tokens = >20000 chars
        var content = "---\nname: foo\n---\n# Big\n" + new string('x', 25000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.Equal("comprehensive", profile.ComplexityTier);
        Assert.Contains(profile.Warnings, w => w.Contains("comprehensive"));
    }

    [Fact]
    public void DetectsWhenToUseSections()
    {
        var content = "---\nname: foo\n---\n# My Skill\n## When to Use\nUse when...\n## When Not to Use\nDon't use when...";
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.True(profile.HasWhenToUse);
        Assert.True(profile.HasWhenNotToUse);
    }

    [Fact]
    public void WarnsWhenNoCodeBlocksPresent()
    {
        var content = "---\nname: foo\n---\n# Title\nJust text, no code.";
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.Contains(profile.Warnings, w => w.Contains("code blocks"));
    }

    [Fact]
    public void ProducesNoWarningsForWellStructuredSkill()
    {
        var content = string.Join("\n",
            "---\nname: good-skill\ndescription: A good skill\n---",
            "# Good Skill",
            "## When to Use",
            "Use when you need to do X.",
            "## Steps",
            "1. First step",
            "2. Second step",
            "3. Third step",
            "```bash",
            "echo hello",
            "```",
            // Pad to ~1500 tokens (6000 chars)
            string.Concat(Enumerable.Repeat("Detailed explanation. ", 250)));
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.Equal("detailed", profile.ComplexityTier);
        Assert.Empty(profile.Warnings);
    }
}

public class FormatProfileLineTests
{
    private static SkillInfo MakeSkill(string content, string name = "test-skill")
    {
        return new SkillInfo(name, "Test skill", "/tmp/test-skill",
            "/tmp/test-skill/SKILL.md", content, null, null);
    }

    [Fact]
    public void ShowsTierIndicator()
    {
        var content = "---\nname: foo\n---\n# Title\n```js\nx\n```\n1. Step\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, "my-skill"));
        var line = SkillProfiler.FormatProfileLine(profile);
        Assert.Contains("my-skill", line);
        Assert.Contains("detailed", line);
        Assert.Contains("✓", line);
    }
}

public class FormatDiagnosisHintsTests
{
    private static SkillInfo MakeSkill(string content)
    {
        return new SkillInfo("test-skill", "Test skill", "/tmp/test-skill",
            "/tmp/test-skill/SKILL.md", content, null, null);
    }

    [Fact]
    public void ReturnsEmptyForSkillsWithNoWarnings()
    {
        var content = string.Join("\n",
            "---\nname: foo\n---",
            "# Title",
            "1. Step",
            "```bash\necho\n```",
            new string('x', 4000));
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.Empty(SkillProfiler.FormatDiagnosisHints(profile));
    }

    [Fact]
    public void ReturnsHintsForSkillsWithWarnings()
    {
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill("tiny"));
        var hints = SkillProfiler.FormatDiagnosisHints(profile);
        Assert.True(hints.Count > 1);
        Assert.Contains("Possible causes", hints[0]);
    }
}
