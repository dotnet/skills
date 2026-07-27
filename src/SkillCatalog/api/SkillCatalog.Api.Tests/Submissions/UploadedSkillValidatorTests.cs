using System.Text;
using SkillCatalog.Api.Models;
using SkillCatalog.Api.Options;
using SkillCatalog.Api.Services;

namespace SkillCatalog.Api.Tests.Submissions;

public sealed class UploadedSkillValidatorTests
{
    [Fact]
    public void Evaluation_and_ownership_findings_are_stable_and_located()
    {
        var validator = Create();
        var files = new Dictionary<string, byte[]>
        {
            ["plugins/dotnet/skills/sample/SKILL.md"] = Encoding.UTF8.GetBytes("# sample\n## Workflow\nDo work.\n## Validation\nCheck."),
            ["tests/dotnet/sample/eval.yaml"] = Encoding.UTF8.GetBytes("scenarios:\n  - prompt: Run sample\n    expect_activation: false\n    graders: []\n    rubric: []\n")
        };
        var preview = new UploadedSkillPreview("dotnet", "sample", "Sample", Encoding.UTF8.GetString(files.First().Value), "new", [], 1, false);
        var findings = validator.Validate(files, "plugins/dotnet/skills/sample/SKILL.md", preview);
        Assert.Contains(findings, x => x.Code == "evaluation.skill-name-leakage" && x.Field.Contains("prompt"));
        Assert.Contains(findings, x => x.Code == "evaluation.positive-required");
        Assert.Contains(findings, x => x.Code == "evaluation.graders");
        Assert.Contains(findings, x => x.Code == "evaluation.rubric");
        Assert.Contains(findings, x => x.Code == "ownership.missing" && x.Severity == "warning");
    }
    private static UploadedSkillValidator Create()
    {
        var options = new SkillCatalogOptions { RepositoryRoot = FindRoot() };
        var limits = new SkillSubmissionOptions();
        return new(new SubmissionRuleProvider(new CatalogSnapshotProvider(options), limits));
    }
    private static string FindRoot() { var dir = new DirectoryInfo(AppContext.BaseDirectory); while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "plugins"))) dir = dir.Parent; return dir?.FullName ?? throw new DirectoryNotFoundException(); }
}
