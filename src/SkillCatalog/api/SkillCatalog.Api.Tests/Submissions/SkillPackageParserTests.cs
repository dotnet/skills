using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Http;
using SkillCatalog.Api.Options;
using SkillCatalog.Api.Services;

namespace SkillCatalog.Api.Tests.Submissions;

public sealed class SkillPackageParserTests
{
    [Fact]
    public async Task Valid_repository_zip_is_parsed_without_extracting()
    {
        var parser = Create();
        var file = Zip(("plugins/dotnet/skills/upload-sample/SKILL.md", Skill("upload-sample")), ("tests/dotnet/upload-sample/eval.yaml", Eval()));
        var (result, files) = await parser.InspectAsync(file, default);
        Assert.True(result.Valid, string.Join("; ", result.Findings.Select(x => $"{x.Code}:{x.Message}")));
        Assert.Equal("upload-sample", result.Preview.Name);
        Assert.Equal(2, files.Count);
    }

    [Theory]
    [InlineData("../escape.txt", "archive.path.unsafe")]
    [InlineData("/rooted.txt", "archive.path.unsafe")]
    public async Task Unsafe_paths_are_rejected(string path, string code)
    {
        var result = await Create().InspectAsync(Zip((path, "bad"), ("plugins/dotnet/skills/upload-sample/SKILL.md", Skill("upload-sample"))), default);
        Assert.Contains(result.Inspection.Findings, x => x.Code == code);
        Assert.False(result.Inspection.Valid);
    }

    [Fact]
    public async Task Missing_reference_and_secret_are_reported()
    {
        var markdown = Skill("upload-sample") + "\n[missing](references/nope.md)\n";
        var result = await Create().InspectAsync(Zip(("plugins/dotnet/skills/upload-sample/SKILL.md", markdown), ("plugins/dotnet/skills/upload-sample/references/config.txt", "api_key=abcdefghijklmnop")), default);
        Assert.Contains(result.Inspection.Findings, x => x.Code == "reference.missing");
        Assert.Contains(result.Inspection.Findings, x => x.Code == "security.credential");
    }

    [Fact]
    public async Task Normalized_package_is_stable_and_uses_safe_paths()
    {
        var parsed = await Create().InspectAsync(Zip(("plugins/dotnet/skills/upload-sample/SKILL.md", Skill("upload-sample"))), default);
        var first = SkillPackageParser.Normalize(parsed.Files);
        var second = SkillPackageParser.Normalize(parsed.Files);
        Assert.Equal(first, second);
        using var archive = new ZipArchive(new MemoryStream(first));
        Assert.All(archive.Entries, x => Assert.DoesNotContain("..", x.FullName));
    }

    private static SkillPackageParser Create()
    {
        var options = new SkillCatalogOptions { RepositoryRoot = FindRoot() };
        var snapshot = new CatalogSnapshotProvider(options);
        var limits = new SkillSubmissionOptions();
        var rules = new SubmissionRuleProvider(snapshot, limits);
        var validator = new UploadedSkillValidator(rules);
        return new(limits, rules, validator);
    }
    private static FormFile Zip(params (string Path, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var item in entries) { var entry = zip.CreateEntry(item.Path); using var writer = new StreamWriter(entry.Open(), Encoding.UTF8); writer.Write(item.Content); }
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "skill.zip") { Headers = new HeaderDictionary(), ContentType = "application/zip" };
    }
    private static string Skill(string name) => $"---\nname: {name}\ndescription: A representative upload validation skill.\n---\n# {name}\n## Workflow\n1. Inspect the input.\n2. Return the result.\n## Validation\nConfirm the result.";
    private static string Eval() => "scenarios:\n  - prompt: Help with this upload\n    expect_activation: true\n    graders:\n      - type: contains\n        substring: result\n    rubric:\n      - The result is correct\n";
    private static string FindRoot() { var dir = new DirectoryInfo(AppContext.BaseDirectory); while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "plugins"))) dir = dir.Parent; return dir?.FullName ?? throw new DirectoryNotFoundException(); }
}
