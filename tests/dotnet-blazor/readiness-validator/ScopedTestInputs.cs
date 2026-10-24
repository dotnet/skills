using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Cli;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Inputs;

internal static class ScopedTestInputs
{
    internal const string Repository = "https://github.com/example-org/sample-controls";
    internal const string Commit = "0123456789abcdef0123456789abcdef01234567";

    internal sealed record Fixture(string Root, InputManifest Input, string InputPath, string CandidatesPath);

    internal static Fixture Create(
        string root, string packageId = "Sample.Controls", string version = "0.1.2-alpha.3")
    {
        Directory.CreateDirectory(root);
        AssessmentTests.CreatePackage(Path.Combine(root, "package.nupkg"), packageId, version);
        var initial = Path.Combine(root, "initial-candidates.json");
        Run("inputs", "candidates", "init", "--acquisition", "published",
            "--package-locator", "package.nupkg", "--package-method", "local-file",
            "--source-availability", "source-available", "--repository-uri", Repository,
            "--source-commit", Commit, "--source-mapping", "Synthetic declared source identity; no product investigation.",
            "--source-confidence", "high", "--output", initial);
        var candidates = Path.Combine(root, "package-candidates.json");
        Run("inputs", "candidates", "add-retrieval", "--input", initial,
            "--subject", "package", "--locator", "package.nupkg", "--method", "local-file",
            "--result", "succeeded", "--output", candidates);
        return Confirm(root, candidates, "unscoped");
    }

    internal static Fixture SelectScope(Fixture fixture)
    {
        Run("inputs", "scope", "--root", fixture.Root, "--manifest", fixture.InputPath,
            "--output", Path.Combine(fixture.Root, AuthorizedPackageScope.Filename));
        var candidates = Path.Combine(fixture.Root, "scoped-candidates.json");
        Run("inputs", "candidates", "add-evidence", "--input", fixture.CandidatesPath,
            "--path", AuthorizedPackageScope.Filename, "--kind", AuthorizedPackageScope.Kind,
            "--output", candidates);
        return Confirm(fixture.Root, candidates, "scoped");
    }

    private static Fixture Confirm(string root, string candidates, string label)
    {
        var draft = Path.Combine(root, label + ".draft.json");
        var confirmed = Path.Combine(root, label + ".confirmed.json");
        Run("inputs", "discover", "--root", root, "--nupkg", Path.Combine(root, "package.nupkg"),
            "--candidates", candidates, "--output", draft);
        Run("inputs", "confirm", "--root", root, "--draft", draft, "--output", confirmed);
        return new(root, InputManifestService.Parse(File.ReadAllBytes(confirmed)), confirmed, candidates);
    }

    private static void Run(params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = CliApplication.Run(arguments, output, error);
        if (exit != 0 || output.ToString().Length != 0 || error.ToString().Length != 0)
            throw new InvalidOperationException($"Synthetic input producer failed ({exit}): {error}\n{output}");
    }
}
