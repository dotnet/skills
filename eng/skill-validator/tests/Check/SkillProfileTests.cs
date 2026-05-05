using SkillValidator.Check;
using SkillValidator.Shared;

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
            SkillMdContent: content);
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
        // >5000 BPE tokens — use varied text since BPE compresses repeated chars efficiently
        var content = "---\nname: foo\n---\n# Big\n" + string.Concat(
            Enumerable.Range(0, 5000).Select(i => $"word{i} "));
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



    [Fact]
    public void DescriptionAtLimitProducesNoError()
    {
        var desc = new string('a', 1024);
        var content = "---\nname: foo\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, description: desc));
        Assert.DoesNotContain(profile.Errors, e => e.Contains("maximum"));
        Assert.DoesNotContain(profile.Errors, e => e.Contains("no description"));
    }

    [Fact]
    public void DescriptionOverLimitErrors()
    {
        var desc = new string('a', 1025);
        var content = "---\nname: foo\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, description: desc));
        Assert.Contains(profile.Errors, e => e.Contains("maximum"));
    }

    [Fact]
    public void EmptyDescriptionWithFrontmatterErrors()
    {
        var content = "---\nname: foo\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, description: "", name: "foo"));
        Assert.Contains(profile.Errors, e => e.Contains("no description"));
    }

    // --- Name validation tests ---

    [Fact]
    public void ValidNameProducesNoNameError()
    {
        var content = "---\nname: my-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "my-skill"));
        Assert.DoesNotContain(profile.Errors, e => e.Contains("Skill name"));
    }

    [Fact]
    public void NameTooLongErrors()
    {
        var longName = new string('a', 65);
        var content = $"---\nname: {longName}\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: longName));
        Assert.Contains(profile.Errors, e => e.Contains("maximum is 64"));
    }

    [Fact]
    public void NameAtLimitNoError()
    {
        var name = new string('a', 64);
        var content = $"---\nname: {name}\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: name));
        Assert.DoesNotContain(profile.Errors, e => e.Contains("maximum is 64"));
    }

    [Fact]
    public void NameWithUppercaseErrors()
    {
        var content = "---\nname: My-Skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "My-Skill"));
        Assert.Contains(profile.Errors, e => e.Contains("invalid characters"));
    }

    [Fact]
    public void NameWithUnderscoreErrors()
    {
        var content = "---\nname: my_skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "my_skill"));
        Assert.Contains(profile.Errors, e => e.Contains("invalid characters"));
    }

    [Fact]
    public void NameStartingWithHyphenErrors()
    {
        var content = "---\nname: -my-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "-my-skill"));
        Assert.Contains(profile.Errors, e => e.Contains("starts or ends with a hyphen"));
    }

    [Fact]
    public void NameEndingWithHyphenErrors()
    {
        var content = "---\nname: my-skill-\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "my-skill-"));
        Assert.Contains(profile.Errors, e => e.Contains("starts or ends with a hyphen"));
    }

    [Fact]
    public void NameWithConsecutiveHyphensErrors()
    {
        var content = "---\nname: my--skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "my--skill"));
        Assert.Contains(profile.Errors, e => e.Contains("consecutive hyphens"));
    }

    [Fact]
    public void NameNotMatchingDirectoryErrors()
    {
        var content = "---\nname: my-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "my-skill", path: "/tmp/different-name"));
        Assert.Contains(profile.Errors, e => e.Contains("does not match directory"));
    }

    [Fact]
    public void NameMatchingDirectoryNoError()
    {
        var content = "---\nname: my-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "my-skill", path: "/tmp/my-skill"));
        Assert.DoesNotContain(profile.Errors, e => e.Contains("does not match directory"));
    }

    // --- Compatibility field tests ---

    [Fact]
    public void CompatibilityOverLimitErrors()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var skill = new SkillInfo("test-skill", "desc", "/tmp/test-skill", "/tmp/test-skill/SKILL.md",
            content, Compatibility: new string('a', 501));
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.Contains(profile.Errors, e => e.Contains("Compatibility") && e.Contains("500"));
    }

    [Fact]
    public void CompatibilityAtLimitNoError()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var skill = new SkillInfo("test-skill", "desc", "/tmp/test-skill", "/tmp/test-skill/SKILL.md",
            content, Compatibility: new string('a', 500));
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.DoesNotContain(profile.Errors, e => e.Contains("Compatibility"));
    }

    [Fact]
    public void CompatibilityEmptyStringErrors()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var skill = new SkillInfo("test-skill", "desc", "/tmp/test-skill", "/tmp/test-skill/SKILL.md",
            content, Compatibility: string.Empty);
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.Contains(profile.Errors, e => e.Contains("Compatibility"));
    }

    // --- File reference depth tests ---

    [Fact]
    public void DeepFileReferenceErrors()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](deep/nested/file.md)\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.Contains(profile.Errors, e => e.Contains("deep/nested/file.md") && e.Contains("directories deep"));
    }

    [Fact]
    public void ShallowFileReferenceNoError()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](references/file.md)\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.DoesNotContain(profile.Errors, e => e.Contains("directories deep") || e.Contains("traversal"));
    }

    [Fact]
    public void HttpLinksNotFlaggedAsDeepRefs()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [docs](https://example.com/a/b/c)\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.DoesNotContain(profile.Errors, e => e.Contains("directories deep") || e.Contains("traversal"));
    }

    [Fact]
    public void ParentDirectoryTraversalErrors()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](../other-skill/SKILL.md)\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.Contains(profile.Errors, e => e.Contains("parent-directory traversal"));
    }

    [Fact]
    public void AnchorFragmentStrippedFromDepthCheck()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](references/file.md#section)\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.DoesNotContain(profile.Errors, e => e.Contains("directories deep") || e.Contains("traversal"));
    }

    [Fact]
    public void DotSlashPrefixNormalizedInDepthCheck()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](./references/file.md)\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.DoesNotContain(profile.Errors, e => e.Contains("directories deep") || e.Contains("traversal"));
    }

    // --- CheckOptions: AllowRepoTraversal ---

    [Fact]
    public void AllowRepoTraversalSuppressesParentTraversalError()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](../SKILL.md)\n" + new string('x', 4000);
        var options = new CheckOptions { AllowRepoTraversal = true };
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content), options);
        Assert.DoesNotContain(profile.Errors, e => e.Contains("traversal") || e.Contains("directories deep"));
    }

    [Fact]
    public void AllowRepoTraversalAllowsDeepExternalRefs()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](../../../documentation/guides/setup.md)\n" + new string('x', 4000);
        var options = new CheckOptions { AllowRepoTraversal = true };
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content), options);
        Assert.DoesNotContain(profile.Errors, e => e.Contains("traversal") || e.Contains("directories deep"));
    }

    [Fact]
    public void AllowRepoTraversalStillChecksDepthForInternalRefs()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](refs/utils/foo/readme.md)\n" + new string('x', 4000);
        var options = new CheckOptions { AllowRepoTraversal = true };
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content), options);
        Assert.Contains(profile.Errors, e => e.Contains("directories deep"));
    }

    // --- Absolute (repo-rooted) path handling ---

    public enum LinkExpectation
    {
        Pass,
        DepthError,
        ParentTraversalError,
        AbsolutePathError,
    }

    /// <summary>
    /// Path-classification contract matrix. Locks down the four kinds of file references
    /// the validator distinguishes, against both flag settings.
    ///
    /// Skill-relative forms (always permitted, no flag needed):
    ///   - sibling files                               file.md, ./file.md
    ///   - one directory below SKILL.md                references/file.md, references/file.md#anchor
    /// Skill-relative but too deep (rejected by design, even with the flag — keeps skills self-contained):
    ///   - more than 1 dir below SKILL.md              deep/nested/file.md
    /// Repo-root-scoped forms (only valid with --allow-repo-traversal):
    ///   - leading-slash absolute                      /src/file.md, /src/libraries/Common/Interop/
    ///   - parent traversal                            ../sibling.md, ../../up/two.md
    /// Always permitted (orthogonal to the flag):
    ///   - in-page anchors                             #section
    ///   - HTTP(S) URLs                                https://example.com/a/b/c
    ///   - protocol-relative URLs (treated as URL)     //github.com/dotnet/runtime
    /// </summary>
    [Theory]
    // --- Skill-relative (always pass, both flag values) ---
    [InlineData("file.md",                                false, LinkExpectation.Pass)]
    [InlineData("file.md",                                true,  LinkExpectation.Pass)]
    [InlineData("./file.md",                              false, LinkExpectation.Pass)]
    [InlineData("./file.md",                              true,  LinkExpectation.Pass)]
    [InlineData("references/file.md",                     false, LinkExpectation.Pass)]
    [InlineData("references/file.md",                     true,  LinkExpectation.Pass)]
    [InlineData("references/file.md#section",             false, LinkExpectation.Pass)]
    [InlineData("references/file.md#section",             true,  LinkExpectation.Pass)]
    // --- Skill-relative but too deep (rejected regardless of flag — internal-portability rule) ---
    [InlineData("deep/nested/file.md",                    false, LinkExpectation.DepthError)]
    [InlineData("deep/nested/file.md",                    true,  LinkExpectation.DepthError)]
    // --- Parent traversal (allowed iff flag) ---
    [InlineData("../sibling.md",                          false, LinkExpectation.ParentTraversalError)]
    [InlineData("../sibling.md",                          true,  LinkExpectation.Pass)]
    [InlineData("../../deep/file.md",                     false, LinkExpectation.ParentTraversalError)]
    [InlineData("../../deep/file.md",                     true,  LinkExpectation.Pass)]
    // --- Absolute repo-rooted (allowed iff flag) ---
    [InlineData("/src/file.md",                           false, LinkExpectation.AbsolutePathError)]
    [InlineData("/src/file.md",                           true,  LinkExpectation.Pass)]
    [InlineData("/src/libraries/Common/src/Interop/",     false, LinkExpectation.AbsolutePathError)]
    [InlineData("/src/libraries/Common/src/Interop/",     true,  LinkExpectation.Pass)]
    // --- Always permitted, independent of flag ---
    [InlineData("#anchor",                                false, LinkExpectation.Pass)]
    [InlineData("#anchor",                                true,  LinkExpectation.Pass)]
    [InlineData("https://example.com/a/b/c",              false, LinkExpectation.Pass)]
    [InlineData("https://example.com/a/b/c",              true,  LinkExpectation.Pass)]
    [InlineData("//github.com/dotnet/runtime",            false, LinkExpectation.Pass)]
    [InlineData("//github.com/dotnet/runtime",            true,  LinkExpectation.Pass)]
    public void PathClassificationMatrix(string refPath, bool allowRepoTraversal, LinkExpectation expected)
    {
        var content = $"---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref]({refPath})\n" + new string('x', 4000);
        var options = new CheckOptions { AllowRepoTraversal = allowRepoTraversal };
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content), options);
        var errorsForThisRef = profile.Errors.Where(e => e.Contains($"'{refPath}'")).ToList();

        switch (expected)
        {
            case LinkExpectation.Pass:
                Assert.Empty(errorsForThisRef);
                break;
            case LinkExpectation.DepthError:
                Assert.Contains(errorsForThisRef, e => e.Contains("directories deep"));
                Assert.DoesNotContain(errorsForThisRef, e => e.Contains("traversal") || e.Contains("absolute"));
                break;
            case LinkExpectation.ParentTraversalError:
                Assert.Contains(errorsForThisRef, e => e.Contains("parent-directory traversal"));
                Assert.DoesNotContain(errorsForThisRef, e => e.Contains("directories deep") || e.Contains("absolute"));
                break;
            case LinkExpectation.AbsolutePathError:
                Assert.Contains(errorsForThisRef, e => e.Contains("absolute (repo-rooted) path"));
                Assert.DoesNotContain(errorsForThisRef, e => e.Contains("directories deep") || e.Contains("parent-directory traversal"));
                break;
        }
    }
}

public class FormatProfileLineTests
{
    private static SkillInfo MakeSkill(string content, string name = "test-skill", string description = "Test skill")
    {
        return new SkillInfo(name, description, "/tmp/test-skill",
            "/tmp/test-skill/SKILL.md", content);
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
            "/tmp/test-skill/SKILL.md", content);
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

public class MinDescriptionLengthTests
{
    private static SkillInfo MakeSkill(string content, string name = "test-skill", string description = "Test skill", string? path = null)
    {
        return new SkillInfo(
            Name: name,
            Description: description,
            Path: path ?? $"/tmp/{name}",
            SkillMdPath: $"{path ?? $"/tmp/{name}"}/SKILL.md",
            SkillMdContent: content);
    }

    [Fact]
    public void DescriptionTooShortErrors()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, description: "Short"));
        Assert.Contains(profile.Errors, e => e.Contains("minimum is 10"));
    }

    [Fact]
    public void DescriptionAtMinimumNoError()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, description: "1234567890"));
        Assert.DoesNotContain(profile.Errors, e => e.Contains("minimum"));
    }

    [Fact]
    public void DescriptionOneCharErrors()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, description: "X"));
        Assert.Contains(profile.Errors, e => e.Contains("minimum is 10"));
    }

    [Fact]
    public void ValidateDescription_TooShort_Errors()
    {
        var errors = new List<string>();
        SkillProfiler.ValidateDescription("Short", "Agent", errors);
        Assert.Contains(errors, e => e.Contains("minimum is 10"));
    }

    [Fact]
    public void ValidateDescription_AtMinimum_NoError()
    {
        var errors = new List<string>();
        SkillProfiler.ValidateDescription("1234567890", "Agent", errors);
        Assert.DoesNotContain(errors, e => e.Contains("minimum"));
    }
}

public class BundledAssetSizeTests : IDisposable
{
    private readonly string _root;

    public BundledAssetSizeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"asset-test-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private SkillInfo MakeSkillWithAsset(string assetDirName, string fileName, long fileSize)
    {
        var skillDir = Path.Combine(_root, "test-skill");
        var assetDir = Path.Combine(skillDir, assetDirName);
        Directory.CreateDirectory(assetDir);

        var filePath = Path.Combine(assetDir, fileName);
        using (var fs = new FileStream(filePath, FileMode.Create))
        {
            fs.SetLength(fileSize);
        }

        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var skillMdPath = Path.Combine(skillDir, "SKILL.md");
        File.WriteAllText(skillMdPath, content);

        return new SkillInfo(
            Name: "test-skill",
            Description: "Valid test skill description",
            Path: skillDir,
            SkillMdPath: skillMdPath,
            SkillMdContent: content);
    }

    [Theory]
    [InlineData("references")]
    [InlineData("assets")]
    [InlineData("scripts")]
    public void AssetOverSizeLimit_Errors(string assetDir)
    {
        var skill = MakeSkillWithAsset(assetDir, "large-file.bin", 6 * 1024 * 1024);
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.Contains(profile.Errors, e => e.Contains("large-file.bin") && e.Contains("5 MB"));
    }

    [Fact]
    public void AssetUnderSizeLimit_NoError()
    {
        var skill = MakeSkillWithAsset("references", "small-file.md", 1024);
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.DoesNotContain(profile.Errors, e => e.Contains("Bundled asset"));
    }

    [Fact]
    public void AssetAtExactLimit_NoError()
    {
        var skill = MakeSkillWithAsset("references", "exact.bin", 5 * 1024 * 1024);
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.DoesNotContain(profile.Errors, e => e.Contains("Bundled asset"));
    }

    [Fact]
    public void NoAssetDirs_NoError()
    {
        var skillDir = Path.Combine(_root, "no-assets-skill");
        Directory.CreateDirectory(skillDir);
        var content = "---\nname: no-assets-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var skillMdPath = Path.Combine(skillDir, "SKILL.md");
        File.WriteAllText(skillMdPath, content);

        var skill = new SkillInfo("no-assets-skill", "Valid test description", skillDir, skillMdPath, content);
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.DoesNotContain(profile.Errors, e => e.Contains("Bundled asset"));
    }
}

