using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Rendering;

namespace BlazorComponentReadiness.Validator.Cli;

public static class ReaderCommand
{
    internal static Action? BeforePublishForTests { get; set; }

    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count == 0 || args[0] is "--help" or "-h")
        {
            output.WriteLine("""
                Deterministic partner-preview reader (does not alter canonical revisions)

                Usage:
                  readiness-validator reader render --root <input-root> --revision <relative-or-absolute-directory> --output <new-directory> [--feedback <file>] [--package-revision <directory>] [--package-feedback <file>]
                  readiness-validator reader verify --root <input-root> --revision <relative-or-absolute-directory> --output <existing-directory> [--feedback <file>] [--package-revision <directory>] [--package-feedback <file>]

                Paths resolve beneath --root, independently of the current working directory.
                The output must be outside all source revision lineages. Verification recomputes
                every byte from the verified source, rubric, feedback and retained evidence.
                Render uses reader 1.0.1; verify regenerates the declared 1.0.0 or 1.0.1 version.
                This local preview is not publication or permission to share private inputs.
                Scoped-component V1 requires --package-context-revision <directory> instead of
                --package-revision; context feedback uses --package-context-feedback <file>.
                This scoped export omits raw inputs and is not a self-contained validation bundle.
                """);
            return ExitCodes.Success;
        }
        if (args[0] is not ("render" or "verify")) throw new UsageException("Unknown reader operation.");
        var options = CommandOptions.Parse(args.Skip(1).ToArray(), "--root", "--revision", "--output",
            "--feedback", "--package-revision", "--package-feedback",
            "--package-context-revision", "--package-context-feedback");
        var root = Path.GetFullPath(options.Single("--root"));
        var revisionPath = Resolve(root, options.Single("--revision"), true);
        var outputPath = Resolve(root, options.Single("--output"), args[0] == "verify");
        var packagePath = options.Optional("--package-revision") is { } packageValue ? Resolve(root, packageValue, true) : null;
        var contextPath = options.Optional("--package-context-revision") is { } contextValue ? Resolve(root, contextValue, true) : null;
        var feedback = Snapshot(root, options.Optional("--feedback"));
        var packageFeedback = Snapshot(root, options.Optional("--package-feedback"));
        var contextFeedback = Snapshot(root, options.Optional("--package-context-feedback"));
        var existingReaderManifest = args[0] == "verify"
            ? ImmutableInputSnapshot.Capture(
                SafePath.ResolveUnderRoot(outputPath, "reader.validation.json", true, true),
                "reader validation manifest")
            : null;
        var readerVersion = ReaderService.Version;
        if (existingReaderManifest is not null)
        {
            using var manifest = StrictJson.Parse(existingReaderManifest.Bytes,
                ResourceLimits.SerializedArtifactBytes, "reader validation manifest");
            ContractJson.RequireProperties(manifest.RootElement, "reader_version", "source_validation_sha256",
                "rubric_sha256", "package_validation_sha256", "files");
            readerVersion = ReaderService.RequireSupportedVersion(
                ContractJson.String(manifest.RootElement, "reader_version"));
        }
        RejectOverlap(outputPath, Path.GetDirectoryName(revisionPath)!);
        if (packagePath is not null) RejectOverlap(outputPath, Path.GetDirectoryName(packagePath)!);
        if (contextPath is not null) RejectOverlap(outputPath, Path.GetDirectoryName(contextPath)!);
        foreach (var snapshot in new[] { feedback, packageFeedback, contextFeedback }.OfType<ImmutableInputSnapshot>())
        {
            if (Within(snapshot.Path, Path.GetDirectoryName(revisionPath)!) ||
                packagePath is not null && Within(snapshot.Path, Path.GetDirectoryName(packagePath)!) ||
                contextPath is not null && Within(snapshot.Path, Path.GetDirectoryName(contextPath)!) ||
                Within(snapshot.Path, outputPath))
                throw new DeterministicValidationException("Reader feedback must remain outside source revisions and derived output.");
        }

        SortedDictionary<string, byte[]> Generate()
        {
            existingReaderManifest?.EnsureUnchanged();
            feedback?.EnsureUnchanged();
            packageFeedback?.EnsureUnchanged();
            contextFeedback?.EnsureUnchanged();
            var input = AssessmentBindingOptions.ReadRevisionInput(root, revisionPath);
            var bindings = AssessmentBindingOptions.Load(root, RevisionService.DetectKind(root, revisionPath), input,
                packagePath, packageFeedback?.Bytes, contextPath, contextFeedback?.Bytes);
            var package = packagePath is null ? null : RevisionService.VerifyRevision(root, packagePath,
                packageFeedback?.Bytes, null, validateChain: true);
            var source = RevisionService.VerifyRevision(root, revisionPath, feedback?.Bytes, bindings.Package,
                validateChain: true, scopedPackageContext: bindings.Context);
            var parsedFeedback = feedback is null ? null :
                FeedbackService.Parse(feedback.Bytes, source.Assessment, package?.Assessment.SelectedIds);
            return ReaderService.Build(root, source, parsedFeedback, feedback?.Bytes, package, bindings.Context, readerVersion);
        }

        var files = Generate();
        if (args[0] == "verify")
        {
            CompareDirectory(outputPath, files);
            CompareFiles(files, Generate());
            return ExitCodes.Success;
        }
        var parent = Path.GetDirectoryName(outputPath)!;
        if (!Directory.Exists(parent))
            throw new DeterministicValidationException("Reader output parent must already exist beneath the input root.");
        AtomicDirectory.WriteNew(parent, Path.GetFileName(outputPath), staging =>
        {
            foreach (var file in files)
            {
                var path = Path.Combine(staging, file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, file.Value);
            }
            BeforePublishForTests?.Invoke();
            CompareFiles(files, Generate());
            CompareDirectory(staging, files);
        });
        return ExitCodes.Success;
    }

    private static void CompareFiles(SortedDictionary<string, byte[]> expected, SortedDictionary<string, byte[]> actual)
    {
        if (!expected.Keys.SequenceEqual(actual.Keys) ||
            expected.Any(file => !file.Value.AsSpan().SequenceEqual(actual[file.Key])))
            throw new DeterministicValidationException("Reader source inputs changed during projection.");
    }

    private static void CompareDirectory(string root, SortedDictionary<string, byte[]> expected)
    {
        var actual = Enumerate(root, root).ToDictionary(path =>
            Path.GetRelativePath(root, path).Replace('\\', '/'), StringComparer.Ordinal);
        var expectedDirectories = expected.Keys.SelectMany(path =>
        {
            var parts = path.Split('/');
            return Enumerable.Range(1, parts.Length - 1).Select(length => string.Join('/', parts.Take(length)));
        }).ToHashSet(StringComparer.Ordinal);
        var actualDirectories = Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        if (!actual.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expected.Keys) ||
            !actualDirectories.SetEquals(expectedDirectories))
            throw new DeterministicValidationException("Reader output inventory differs from the deterministic projection.");
        foreach (var file in expected)
            if (!BoundedIO.ReadAllBytes(actual[file.Key], ResourceLimits.NupkgBytes, "reader artifact")
                .AsSpan().SequenceEqual(file.Value))
                throw new DeterministicValidationException($"Reader artifact differs from its exact source projection: {file.Key}.");
    }

    private static IEnumerable<string> Enumerate(string root, string directory)
    {
        foreach (var file in Directory.GetFiles(directory))
            yield return SafePath.ResolveUnderRoot(root, Path.GetRelativePath(root, file), true, true);
        foreach (var child in Directory.GetDirectories(directory))
        {
            _ = SafePath.ResolveUnderRoot(root, Path.GetRelativePath(root, child), true);
            foreach (var file in Enumerate(root, child)) yield return file;
        }
    }

    private static string Resolve(string root, string path, bool exists) =>
        SafePath.ResolveUnderRoot(root, Path.IsPathRooted(path) ? Path.GetRelativePath(root, path) : path, exists);

    private static ImmutableInputSnapshot? Snapshot(string root, string? path) =>
        path is null ? null : ImmutableInputSnapshot.Capture(Resolve(root, path, true), "reader feedback");

    private static bool Within(string path, string parent)
    {
        var relative = Path.GetRelativePath(parent, path);
        return relative == "." || !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static void RejectOverlap(string output, string revisions)
    {
        if (Within(output, revisions) || Within(revisions, output))
            throw new DeterministicValidationException("Reader output must be separate from immutable revision lineages.");
    }
}
