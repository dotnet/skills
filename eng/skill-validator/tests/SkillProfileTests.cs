using SkillValidator.Models;
using SkillValidator.Services;

namespace SkillValidator.Tests;

public class AnalyzeSkillTests
{
    private static SkillInfo MakeSkill(string content, string name = "test-skill", string description = "Test skill", string? path = null)
    {
        return new SkillInfo(
            Name: name,
            Description: description,
            Path: path ?? $"/tmp/{name}",
            SkillMdPath: $"{path ?? $"/tmp/{name}"}/SKILL.md",
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

    private static SkillInfo MakeSkillWithEval(string content, string name, List<EvalScenario> scenarios)
    {
        return new SkillInfo(
            Name: name,
            Description: "Test skill",
            Path: "/tmp/test-skill",
            SkillMdPath: "/tmp/test-skill/SKILL.md",
            SkillMdContent: content,
            EvalPath: "/tmp/test-skill/eval.yaml",
            EvalConfig: new EvalConfig(scenarios));
    }

    [Fact]
    public void WarnsWhenEvalPromptMentionsSkillName()
    {
        var content = "---\nname: migrate-app\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var scenarios = new List<EvalScenario>
        {
            new("basic", "Use the migrate-app skill to help me migrate my project")
        };
        var skill = MakeSkillWithEval(content, "migrate-app", scenarios);
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.Contains(profile.Warnings, w => w.Contains("mentions skill name") && w.Contains("migrate-app"));
    }

    [Fact]
    public void NoWarningWhenEvalPromptDoesNotMentionSkillName()
    {
        var content = "---\nname: migrate-app\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var scenarios = new List<EvalScenario>
        {
            new("basic", "Help me migrate my project to the latest framework")
        };
        var skill = MakeSkillWithEval(content, "migrate-app", scenarios);
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.DoesNotContain(profile.Warnings, w => w.Contains("mentions skill name"));
    }

    [Fact]
    public void NoWarningWhenSkillNameIsEmpty()
    {
        var content = "---\nname: \n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var scenarios = new List<EvalScenario>
        {
            new("basic", "Help me migrate my project")
        };
        var skill = MakeSkillWithEval(content, "", scenarios);
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.DoesNotContain(profile.Warnings, w => w.Contains("mentions skill name"));
    }

    [Fact]
    public void DescriptionAtLimitProducesNoWarning()
    {
        var desc = new string('a', 1024);
        var content = "---\nname: foo\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, description: desc));
        Assert.False(profile.DescriptionTooLong);
        Assert.DoesNotContain(profile.Warnings, w => w.Contains("maximum") || w.Contains("no description"));
    }

    [Fact]
    public void DescriptionOverLimitWarnsAndFlags()
    {
        var desc = new string('a', 1025);
        var content = "---\nname: foo\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, description: desc));
        Assert.True(profile.DescriptionTooLong);
        Assert.Contains(profile.Warnings, w => w.Contains("maximum"));
    }

    [Fact]
    public void EmptyDescriptionWithFrontmatterWarns()
    {
        var content = "---\nname: foo\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, description: "", name: "foo"));
        Assert.Contains(profile.Warnings, w => w.Contains("no description"));
    }

    // --- Name validation tests ---

    [Fact]
    public void ValidNameProducesNoNameWarning()
    {
        var content = "---\nname: my-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "my-skill"));
        Assert.DoesNotContain(profile.Warnings, w => w.Contains("Skill name"));
    }

    [Fact]
    public void NameTooLongWarns()
    {
        var longName = new string('a', 65);
        var content = $"---\nname: {longName}\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: longName));
        Assert.Contains(profile.Warnings, w => w.Contains("maximum is 64"));
    }

    [Fact]
    public void NameAtLimitNoWarning()
    {
        var name = new string('a', 64);
        var content = $"---\nname: {name}\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: name));
        Assert.DoesNotContain(profile.Warnings, w => w.Contains("maximum is 64"));
    }

    [Fact]
    public void NameWithUppercaseWarns()
    {
        var content = "---\nname: My-Skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "My-Skill"));
        Assert.Contains(profile.Warnings, w => w.Contains("invalid characters"));
    }

    [Fact]
    public void NameWithUnderscoreWarns()
    {
        var content = "---\nname: my_skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "my_skill"));
        Assert.Contains(profile.Warnings, w => w.Contains("invalid characters"));
    }

    [Fact]
    public void NameStartingWithHyphenWarns()
    {
        var content = "---\nname: -my-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "-my-skill"));
        Assert.Contains(profile.Warnings, w => w.Contains("starts or ends with a hyphen"));
    }

    [Fact]
    public void NameEndingWithHyphenWarns()
    {
        var content = "---\nname: my-skill-\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "my-skill-"));
        Assert.Contains(profile.Warnings, w => w.Contains("starts or ends with a hyphen"));
    }

    [Fact]
    public void NameWithConsecutiveHyphensWarns()
    {
        var content = "---\nname: my--skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "my--skill"));
        Assert.Contains(profile.Warnings, w => w.Contains("consecutive hyphens"));
    }

    [Fact]
    public void NameNotMatchingDirectoryWarns()
    {
        var content = "---\nname: my-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "my-skill", path: "/tmp/different-name"));
        Assert.Contains(profile.Warnings, w => w.Contains("does not match directory"));
    }

    [Fact]
    public void NameMatchingDirectoryNoWarning()
    {
        var content = "---\nname: my-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "my-skill", path: "/tmp/my-skill"));
        Assert.DoesNotContain(profile.Warnings, w => w.Contains("does not match directory"));
    }

    // --- Compatibility field tests ---

    [Fact]
    public void CompatibilityOverLimitWarns()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var skill = new SkillInfo("test-skill", "desc", "/tmp/test-skill", "/tmp/test-skill/SKILL.md",
            content, null, null, Compatibility: new string('a', 501));
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.Contains(profile.Warnings, w => w.Contains("Compatibility") && w.Contains("500"));
    }

    [Fact]
    public void CompatibilityAtLimitNoWarning()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var skill = new SkillInfo("test-skill", "desc", "/tmp/test-skill", "/tmp/test-skill/SKILL.md",
            content, null, null, Compatibility: new string('a', 500));
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.DoesNotContain(profile.Warnings, w => w.Contains("Compatibility"));
    }

    // --- Body line count tests ---

    [Fact]
    public void BodyOver500LinesWarns()
    {
        var body = string.Join("\n", Enumerable.Range(1, 501).Select(i => $"Line {i}"));
        var content = "---\nname: test-skill\n---\n" + body;
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.Contains(profile.Warnings, w => w.Contains("lines") && w.Contains("500"));
    }

    [Fact]
    public void BodyAt500LinesNoWarning()
    {
        var body = string.Join("\n", Enumerable.Range(1, 500).Select(i => $"Line {i}"));
        var content = "---\nname: test-skill\n---\n" + body;
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.DoesNotContain(profile.Warnings, w => w.Contains("lines") && w.Contains("500"));
    }

    [Fact]
    public void BodyAt500LinesWithTrailingNewlineNoWarning()
    {
        var body = string.Join("\n", Enumerable.Range(1, 500).Select(i => $"Line {i}")) + "\n";
        var content = "---\nname: test-skill\n---\n" + body;
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.DoesNotContain(profile.Warnings, w => w.Contains("lines") && w.Contains("500"));
    }

    // --- File reference depth tests ---

    [Fact]
    public void DeepFileReferenceWarns()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](deep/nested/file.md)\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.Contains(profile.Warnings, w => w.Contains("deep/nested/file.md") && w.Contains("directories deep"));
    }

    [Fact]
    public void ShallowFileReferenceNoWarning()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](references/file.md)\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.DoesNotContain(profile.Warnings, w => w.Contains("directories deep") || w.Contains("traversal"));
    }

    [Fact]
    public void HttpLinksNotFlaggedAsDeepRefs()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [docs](https://example.com/a/b/c)\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.DoesNotContain(profile.Warnings, w => w.Contains("directories deep") || w.Contains("traversal"));
    }

    [Fact]
    public void ParentDirectoryTraversalWarns()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](../other-skill/SKILL.md)\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.Contains(profile.Warnings, w => w.Contains("parent-directory traversal"));
    }

    [Fact]
    public void AnchorFragmentStrippedFromDepthCheck()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](references/file.md#section)\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.DoesNotContain(profile.Warnings, w => w.Contains("directories deep") || w.Contains("traversal"));
    }

    [Fact]
    public void DotSlashPrefixNormalizedInDepthCheck()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](./references/file.md)\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.DoesNotContain(profile.Warnings, w => w.Contains("directories deep") || w.Contains("traversal"));
    }
}

public class FormatProfileLineTests
{
    private static SkillInfo MakeSkill(string content, string name = "test-skill", string description = "Test skill")
    {
        return new SkillInfo(name, description, "/tmp/test-skill",
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
    private static SkillInfo MakeSkill(string content, string description = "Test skill")
    {
        return new SkillInfo("test-skill", description, "/tmp/test-skill",
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
