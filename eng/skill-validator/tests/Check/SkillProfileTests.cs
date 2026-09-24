using SkillValidator.Check;
using SkillValidator.Shared;

namespace SkillValidator.Tests;

[TestClass]
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

    [TestMethod]
    public void DetectsFrontmatter()
    {
        var skill = MakeSkill("---\nname: foo\n---\n# Hello\nSome content");
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.IsTrue(profile.HasFrontmatter);
    }

    [TestMethod]
    public void DetectsMissingFrontmatter()
    {
        var skill = MakeSkill("# Hello\nSome content");
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.IsFalse(profile.HasFrontmatter);
        Assert.IsTrue((profile.Warnings).Any(w => w.Contains("frontmatter")));
    }

    [TestMethod]
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
        Assert.AreEqual(3, profile.SectionCount);
        Assert.AreEqual(3, profile.CodeBlockCount);
    }

    [TestMethod]
    public void CountsNumberedSteps()
    {
        var content = "---\nname: foo\n---\n# Steps\n1. First\n2. Second\n3. Third\n";
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.AreEqual(3, profile.NumberedStepCount);
    }

    [TestMethod]
    public void ClassifiesCompactSkills()
    {
        var content = "---\nname: foo\n---\n# Short\nBrief.";
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.AreEqual("compact", profile.ComplexityTier);
    }

    [TestMethod]
    public void ClassifiesComprehensiveSkillsAndWarns()
    {
        // >5000 BPE tokens — use varied text since BPE compresses repeated chars efficiently
        var content = "---\nname: foo\n---\n# Big\n" + string.Concat(
            Enumerable.Range(0, 5000).Select(i => $"word{i} "));
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.AreEqual("comprehensive", profile.ComplexityTier);
        Assert.IsTrue((profile.Warnings).Any(w => w.Contains("comprehensive")));
    }

    [TestMethod]
    public void DetectsWhenToUseSections()
    {
        var content = "---\nname: foo\n---\n# My Skill\n## When to Use\nUse when...\n## When Not to Use\nDon't use when...";
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.IsTrue(profile.HasWhenToUse);
        Assert.IsTrue(profile.HasWhenNotToUse);
    }

    [TestMethod]
    public void WarnsWhenNoCodeBlocksPresent()
    {
        var content = "---\nname: foo\n---\n# Title\nJust text, no code.";
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.IsTrue((profile.Warnings).Any(w => w.Contains("code blocks")));
    }

    [TestMethod]
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
        Assert.AreEqual("detailed", profile.ComplexityTier);
        Assert.IsEmpty(profile.Warnings);
    }



    [TestMethod]
    public void DescriptionAtLimitProducesNoError()
    {
        var desc = new string('a', 1024);
        var content = "---\nname: foo\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, description: desc));
        Assert.IsFalse((profile.Errors).Any(e => e.Contains("maximum")));
        Assert.IsFalse((profile.Errors).Any(e => e.Contains("no description")));
    }

    [TestMethod]
    public void DescriptionOverLimitErrors()
    {
        var desc = new string('a', 1025);
        var content = "---\nname: foo\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, description: desc));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("maximum")));
    }

    [TestMethod]
    public void EmptyDescriptionWithFrontmatterErrors()
    {
        var content = "---\nname: foo\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, description: "", name: "foo"));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("no description")));
    }

    // --- Name validation tests ---

    [TestMethod]
    public void ValidNameProducesNoNameError()
    {
        var content = "---\nname: my-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "my-skill"));
        Assert.IsFalse((profile.Errors).Any(e => e.Contains("Skill name")));
    }

    [TestMethod]
    public void NameTooLongErrors()
    {
        var longName = new string('a', 65);
        var content = $"---\nname: {longName}\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: longName));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("maximum is 64")));
    }

    [TestMethod]
    public void NameAtLimitNoError()
    {
        var name = new string('a', 64);
        var content = $"---\nname: {name}\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: name));
        Assert.IsFalse((profile.Errors).Any(e => e.Contains("maximum is 64")));
    }

    [TestMethod]
    public void NameWithUppercaseErrors()
    {
        var content = "---\nname: My-Skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "My-Skill"));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("invalid characters")));
    }

    [TestMethod]
    public void NameWithUnderscoreErrors()
    {
        var content = "---\nname: my_skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "my_skill"));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("invalid characters")));
    }

    [TestMethod]
    public void NameStartingWithHyphenErrors()
    {
        var content = "---\nname: -my-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "-my-skill"));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("starts or ends with a hyphen")));
    }

    [TestMethod]
    public void NameEndingWithHyphenErrors()
    {
        var content = "---\nname: my-skill-\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "my-skill-"));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("starts or ends with a hyphen")));
    }

    [TestMethod]
    public void NameWithConsecutiveHyphensErrors()
    {
        var content = "---\nname: my--skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "my--skill"));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("consecutive hyphens")));
    }

    [TestMethod]
    public void NameNotMatchingDirectoryErrors()
    {
        var content = "---\nname: my-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "my-skill", path: "/tmp/different-name"));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("does not match directory")));
    }

    [TestMethod]
    public void NameMatchingDirectoryNoError()
    {
        var content = "---\nname: my-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, name: "my-skill", path: "/tmp/my-skill"));
        Assert.IsFalse((profile.Errors).Any(e => e.Contains("does not match directory")));
    }

    // --- Compatibility field tests ---

    [TestMethod]
    public void CompatibilityOverLimitErrors()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var skill = new SkillInfo("test-skill", "desc", "/tmp/test-skill", "/tmp/test-skill/SKILL.md",
            content, Compatibility: new string('a', 501));
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("Compatibility") && e.Contains("500")));
    }

    [TestMethod]
    public void CompatibilityAtLimitNoError()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var skill = new SkillInfo("test-skill", "desc", "/tmp/test-skill", "/tmp/test-skill/SKILL.md",
            content, Compatibility: new string('a', 500));
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.IsFalse((profile.Errors).Any(e => e.Contains("Compatibility")));
    }

    [TestMethod]
    public void CompatibilityEmptyStringErrors()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var skill = new SkillInfo("test-skill", "desc", "/tmp/test-skill", "/tmp/test-skill/SKILL.md",
            content, Compatibility: string.Empty);
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("Compatibility")));
    }

    // --- File reference depth tests ---

    [TestMethod]
    public void DeepFileReferenceErrors()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](deep/nested/file.md)\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("deep/nested/file.md") && e.Contains("directories deep")));
    }

    [TestMethod]
    public void ShallowFileReferenceNoError()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](references/file.md)\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.IsFalse((profile.Errors).Any(e => e.Contains("directories deep") || e.Contains("traversal")));
    }

    [TestMethod]
    public void HttpLinksNotFlaggedAsDeepRefs()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [docs](https://example.com/a/b/c)\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.IsFalse((profile.Errors).Any(e => e.Contains("directories deep") || e.Contains("traversal")));
    }

    [TestMethod]
    public void ParentDirectoryTraversalErrors()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](../other-skill/SKILL.md)\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("parent-directory traversal")));
    }

    [TestMethod]
    public void AnchorFragmentStrippedFromDepthCheck()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](references/file.md#section)\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.IsFalse((profile.Errors).Any(e => e.Contains("directories deep") || e.Contains("traversal")));
    }

    [TestMethod]
    public void DotSlashPrefixNormalizedInDepthCheck()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](./references/file.md)\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.IsFalse((profile.Errors).Any(e => e.Contains("directories deep") || e.Contains("traversal")));
    }

    // --- CheckOptions: AllowRepoTraversal ---

    [TestMethod]
    public void AllowRepoTraversalSuppressesParentTraversalError()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](../SKILL.md)\n" + new string('x', 4000);
        var options = new CheckOptions { AllowRepoTraversal = true };
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content), options);
        Assert.IsFalse((profile.Errors).Any(e => e.Contains("traversal") || e.Contains("directories deep")));
    }

    [TestMethod]
    public void AllowRepoTraversalAllowsDeepExternalRefs()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](../../../documentation/guides/setup.md)\n" + new string('x', 4000);
        var options = new CheckOptions { AllowRepoTraversal = true };
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content), options);
        Assert.IsFalse((profile.Errors).Any(e => e.Contains("traversal") || e.Contains("directories deep")));
    }

    [TestMethod]
    public void AllowRepoTraversalStillChecksDepthForInternalRefs()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref](refs/utils/foo/readme.md)\n" + new string('x', 4000);
        var options = new CheckOptions { AllowRepoTraversal = true };
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content), options);
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("directories deep")));
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
    [TestMethod]
    // --- Skill-relative (always pass, both flag values) ---
    [DataRow("file.md",                                false, LinkExpectation.Pass)]
    [DataRow("file.md",                                true,  LinkExpectation.Pass)]
    [DataRow("./file.md",                              false, LinkExpectation.Pass)]
    [DataRow("./file.md",                              true,  LinkExpectation.Pass)]
    [DataRow("references/file.md",                     false, LinkExpectation.Pass)]
    [DataRow("references/file.md",                     true,  LinkExpectation.Pass)]
    [DataRow("references/file.md#section",             false, LinkExpectation.Pass)]
    [DataRow("references/file.md#section",             true,  LinkExpectation.Pass)]
    // --- Skill-relative but too deep (rejected regardless of flag — internal-portability rule) ---
    [DataRow("deep/nested/file.md",                    false, LinkExpectation.DepthError)]
    [DataRow("deep/nested/file.md",                    true,  LinkExpectation.DepthError)]
    // --- Parent traversal (allowed iff flag) ---
    [DataRow("../sibling.md",                          false, LinkExpectation.ParentTraversalError)]
    [DataRow("../sibling.md",                          true,  LinkExpectation.Pass)]
    [DataRow("../../deep/file.md",                     false, LinkExpectation.ParentTraversalError)]
    [DataRow("../../deep/file.md",                     true,  LinkExpectation.Pass)]
    // --- Absolute repo-rooted (allowed iff flag) ---
    [DataRow("/src/file.md",                           false, LinkExpectation.AbsolutePathError)]
    [DataRow("/src/file.md",                           true,  LinkExpectation.Pass)]
    [DataRow("/src/libraries/Common/src/Interop/",     false, LinkExpectation.AbsolutePathError)]
    [DataRow("/src/libraries/Common/src/Interop/",     true,  LinkExpectation.Pass)]
    // --- Always permitted, independent of flag ---
    [DataRow("#anchor",                                false, LinkExpectation.Pass)]
    [DataRow("#anchor",                                true,  LinkExpectation.Pass)]
    [DataRow("https://example.com/a/b/c",              false, LinkExpectation.Pass)]
    [DataRow("https://example.com/a/b/c",              true,  LinkExpectation.Pass)]
    [DataRow("//github.com/dotnet/runtime",            false, LinkExpectation.Pass)]
    [DataRow("//github.com/dotnet/runtime",            true,  LinkExpectation.Pass)]
    public void PathClassificationMatrix(string refPath, bool allowRepoTraversal, LinkExpectation expected)
    {
        var content = $"---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\nSee [ref]({refPath})\n" + new string('x', 4000);
        var options = new CheckOptions { AllowRepoTraversal = allowRepoTraversal };
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content), options);
        var errorsForThisRef = profile.Errors.Where(e => e.Contains($"'{refPath}'")).ToList();

        switch (expected)
        {
            case LinkExpectation.Pass:
                Assert.IsEmpty(errorsForThisRef);
                break;
            case LinkExpectation.DepthError:
                Assert.IsTrue((errorsForThisRef).Any(e => e.Contains("directories deep")));
                Assert.IsFalse((errorsForThisRef).Any(e => e.Contains("traversal") || e.Contains("absolute")));
                break;
            case LinkExpectation.ParentTraversalError:
                Assert.IsTrue((errorsForThisRef).Any(e => e.Contains("parent-directory traversal")));
                Assert.IsFalse((errorsForThisRef).Any(e => e.Contains("directories deep") || e.Contains("absolute")));
                break;
            case LinkExpectation.AbsolutePathError:
                Assert.IsTrue((errorsForThisRef).Any(e => e.Contains("absolute (repo-rooted) path")));
                Assert.IsFalse((errorsForThisRef).Any(e => e.Contains("directories deep") || e.Contains("parent-directory traversal")));
                break;
        }
    }
}

[TestClass]
public class FormatProfileLineTests
{
    private static SkillInfo MakeSkill(string content, string name = "test-skill", string description = "Test skill")
    {
        return new SkillInfo(name, description, "/tmp/test-skill",
            "/tmp/test-skill/SKILL.md", content);
    }

    [TestMethod]
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

[TestClass]
public class FormatDiagnosisHintsTests
{
    private static SkillInfo MakeSkill(string content, string description = "Test skill")
    {
        return new SkillInfo("test-skill", description, "/tmp/test-skill",
            "/tmp/test-skill/SKILL.md", content);
    }

    [TestMethod]
    public void ReturnsEmptyForSkillsWithNoWarnings()
    {
        var content = string.Join("\n",
            "---\nname: foo\n---",
            "# Title",
            "1. Step",
            "```bash\necho\n```",
            new string('x', 4000));
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content));
        Assert.IsEmpty(SkillProfiler.FormatDiagnosisHints(profile));
    }

    [TestMethod]
    public void ReturnsHintsForSkillsWithWarnings()
    {
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill("tiny"));
        var hints = SkillProfiler.FormatDiagnosisHints(profile);
        Assert.IsTrue(hints.Count > 1);
        Assert.Contains("Possible causes", hints[0]);
    }
}

[TestClass]
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

    [TestMethod]
    public void DescriptionTooShortErrors()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, description: "Short"));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("minimum is 10")));
    }

    [TestMethod]
    public void DescriptionAtMinimumNoError()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, description: "1234567890"));
        Assert.IsFalse((profile.Errors).Any(e => e.Contains("minimum")));
    }

    [TestMethod]
    public void DescriptionOneCharErrors()
    {
        var content = "---\nname: test-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var profile = SkillProfiler.AnalyzeSkill(MakeSkill(content, description: "X"));
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("minimum is 10")));
    }

    [TestMethod]
    public void ValidateDescription_TooShort_Errors()
    {
        var errors = new List<string>();
        SkillProfiler.ValidateDescription("Short", "Agent", errors);
        Assert.IsTrue((errors).Any(e => e.Contains("minimum is 10")));
    }

    [TestMethod]
    public void ValidateDescription_AtMinimum_NoError()
    {
        var errors = new List<string>();
        SkillProfiler.ValidateDescription("1234567890", "Agent", errors);
        Assert.IsFalse((errors).Any(e => e.Contains("minimum")));
    }
}

[TestClass]
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

    [TestMethod]
    [DataRow("references")]
    [DataRow("assets")]
    [DataRow("scripts")]
    public void AssetOverSizeLimit_Errors(string assetDir)
    {
        var skill = MakeSkillWithAsset(assetDir, "large-file.bin", 6 * 1024 * 1024);
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.IsTrue((profile.Errors).Any(e => e.Contains("large-file.bin") && e.Contains("5 MB")));
    }

    [TestMethod]
    public void AssetUnderSizeLimit_NoError()
    {
        var skill = MakeSkillWithAsset("references", "small-file.md", 1024);
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.IsFalse((profile.Errors).Any(e => e.Contains("Bundled asset")));
    }

    [TestMethod]
    public void AssetAtExactLimit_NoError()
    {
        var skill = MakeSkillWithAsset("references", "exact.bin", 5 * 1024 * 1024);
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.IsFalse((profile.Errors).Any(e => e.Contains("Bundled asset")));
    }

    [TestMethod]
    public void NoAssetDirs_NoError()
    {
        var skillDir = Path.Combine(_root, "no-assets-skill");
        Directory.CreateDirectory(skillDir);
        var content = "---\nname: no-assets-skill\n---\n# Title\n1. Step\n```bash\necho\n```\n" + new string('x', 4000);
        var skillMdPath = Path.Combine(skillDir, "SKILL.md");
        File.WriteAllText(skillMdPath, content);

        var skill = new SkillInfo("no-assets-skill", "Valid test description", skillDir, skillMdPath, content);
        var profile = SkillProfiler.AnalyzeSkill(skill);
        Assert.IsFalse((profile.Errors).Any(e => e.Contains("Bundled asset")));
    }
}

