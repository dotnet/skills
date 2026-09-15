using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Rendering;

namespace BlazorComponentReadiness.Validator.Cli;

public static class ReportCommand
{
    internal static Action? BeforePublishForTests { get; set; }

    private const string Help =
        """
        Deterministic readiness reports

        Usage:
          readiness-validator report render --root <input-root> --input <confirmed> --assessment <json> --evidence <bundle> --output <revisions-root> [--feedback <markdown>] [--predecessor <digest>] [--changed-ids <id,id>] [--package-revision <dir>] [--package-feedback <markdown>]
          readiness-validator report verify --root <input-root> --revision <revision-directory> [--feedback <markdown>] [--package-revision <dir>] [--package-feedback <markdown>]

        Scoped-component V1 requires --package-context-revision <dir> instead of the ordinary
        --package-revision. Context feedback uses --package-context-feedback <markdown>.
        Context locators do not authorize a profile or replace its confirmed input bindings.
        """;

    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count == 0 || IsHelp(args[0]))
        {
            output.WriteLine(Help);
            return ExitCodes.Success;
        }

        return args[0] switch
        {
            "render" => Render(args.Skip(1).ToArray(), output),
            "verify" => Verify(args.Skip(1).ToArray(), output),
            _ => throw new UsageException($"Unknown report command '{args[0]}'.")
        };
    }

    internal static int Render(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count == 1 && IsHelp(args[0]))
        {
            output.WriteLine(Help);
            return ExitCodes.Success;
        }

        var options = CommandOptions.Parse(
            args,
            "--root",
            "--input",
            "--assessment",
            "--evidence",
            "--output",
            "--feedback",
            "--predecessor",
            "--changed-ids",
            "--package-revision",
            "--package-feedback",
            "--package-context-revision",
            "--package-context-feedback");
        var root = Path.GetFullPath(options.Single("--root"));
        var inputSnapshot = ImmutableInputSnapshot.Capture(
            Path.GetFullPath(options.Single("--input")),
            "confirmed input manifest");
        var assessmentSnapshot = ImmutableInputSnapshot.Capture(
            Path.GetFullPath(options.Single("--assessment")),
            "assessment");
        var evidenceSnapshot = ImmutableInputSnapshot.Capture(
            Path.GetFullPath(options.Single("--evidence")),
            "evidence bundle");
        var feedbackSnapshot = CaptureOptional(options.Optional("--feedback"), "assessment feedback");
        var packageFeedbackSnapshot = CaptureOptional(
            options.Optional("--package-feedback"),
            "package assessment feedback");
        var contextFeedbackSnapshot = CaptureOptional(
            options.Optional("--package-context-feedback"), "scoped package context feedback");
        var inputBytes = inputSnapshot.Bytes;
        var assessmentBytes = assessmentSnapshot.Bytes;
        var evidenceBytes = evidenceSnapshot.Bytes;
        var input = InputManifestService.Parse(inputBytes);
        InputManifestService.Validate(input, root, requireConfirmed: true);
        var referencedSnapshots = CaptureReferencedInputs(input, root);
        InputManifestService.Validate(input, root, requireConfirmed: true);
        var assessment = AssessmentService.Parse(assessmentBytes);
        var evidence = CanonicalEvidenceJson.ParseBundle(evidenceBytes);
        var bindings = AssessmentBindingOptions.Load(
            root,
            assessment.AssessmentKind,
            input,
            options.Optional("--package-revision"),
            packageFeedbackSnapshot?.Bytes,
            options.Optional("--package-context-revision"),
            contextFeedbackSnapshot?.Bytes);
        var packageBinding = bindings.Package;
        var scopedPackageContext = bindings.Context;
        AssessmentService.Validate(
            root,
            assessment,
            assessmentBytes,
            input,
            inputBytes,
            evidence,
            packageBinding,
            scopedPackageContext);
        var feedback = feedbackSnapshot is null
            ? null
            : FeedbackService.Parse(
                feedbackSnapshot.Bytes,
                assessment,
                packageBinding?.Assessment.SelectedIds);
        var outputRoot = Path.GetFullPath(options.Single("--output"));
        EnsureFeedbackOutsideRevisions(feedbackSnapshot, outputRoot);
        if (options.Optional("--package-revision") is { } packageRevision)
        {
            EnsureFeedbackOutsideRevisions(
                packageFeedbackSnapshot,
                Path.GetDirectoryName(Path.GetFullPath(packageRevision))!);
        }
        if (options.Optional("--package-context-revision") is { } contextRevision)
            EnsureFeedbackOutsideRevisions(contextFeedbackSnapshot,
                Path.GetDirectoryName(SafePath.ResolveExistingDirectoryUnderRoot(root,
                    Path.IsPathRooted(contextRevision) ? contextRevision : Path.Combine(root, contextRevision)))!);

        using var revisionRootLock = RevisionRootLock.Acquire(outputRoot);
        var nextSequence = NextSequence(outputRoot);
        var changedIds = ParseChangedIds(options.Optional("--changed-ids"));
        var predecessorText = options.Optional("--predecessor");
        Sha256Digest? predecessorDigest = null;
        RevisionArtifacts? predecessor = null;
        if (nextSequence == 1)
        {
            if (predecessorText is not null || changedIds.Count != 0)
            {
                throw new DeterministicValidationException(
                    "Revision 0001 cannot declare a predecessor or changed requirement IDs.");
            }
        }
        else
        {
            if (predecessorText is null)
            {
                throw new DeterministicValidationException(
                    "Every revision after 0001 requires the immediate predecessor manifest digest.");
            }

            predecessorDigest = ParseDigest(predecessorText, "predecessor manifest digest");
            var predecessorDirectory = Path.Combine(outputRoot, (nextSequence - 1).ToString("D4"));
            predecessor = RevisionService.VerifyRevision(
                root,
                predecessorDirectory,
                feedbackBytes: null,
                packageBinding,
                validateChain: true,
                allowMissingFeedback: true,
                scopedPackageContext);
            if (ContractJson.RawDigest(predecessor.ManifestBytes) != predecessorDigest)
            {
                throw new DeterministicValidationException(
                    "Declared predecessor is missing, forked, or not the immediate preceding manifest.");
            }
        }

        var reportBytes = ReportService.RenderMarkdown(assessment, input, evidence, feedback, root, scopedPackageContext);
        var manifest = ReportService.CreateManifest(
            assessment,
            assessmentBytes,
            input,
            inputBytes,
            evidence,
            evidenceBytes,
            reportBytes,
            predecessorDigest,
            feedback?.Digest,
            changedIds,
            root,
            scopedPackageContext);
        var manifestBytes = ReportService.SerializeManifest(manifest);
        var revisionName = nextSequence.ToString("D4");
        var replacement = new RevisionArtifacts(
            Path.Combine(outputRoot, revisionName),
            assessment.AssessmentKind,
            input,
            inputBytes,
            assessment,
            assessmentBytes,
            evidence,
            evidenceBytes,
            reportBytes,
            manifest,
            manifestBytes);
        if (predecessor is not null)
        {
            if (changedIds.Count == 0)
            {
                if (!inputBytes.AsSpan().SequenceEqual(predecessor.InputBytes) ||
                    !assessmentBytes.AsSpan().SequenceEqual(predecessor.AssessmentBytes) ||
                    !evidenceBytes.AsSpan().SequenceEqual(predecessor.EvidenceBytes))
                {
                    throw new DeterministicValidationException(
                        "Feedback-only revisions must preserve input, assessment, and evidence bytes exactly.");
                }
            }
            else
            {
                RevisionService.ValidateCorrection(predecessor, replacement, changedIds);
            }
        }

        AtomicDirectory.WriteNew(outputRoot, revisionName, staging =>
        {
            File.WriteAllBytes(Path.Combine(staging, "input-manifest.json"), inputBytes);
            File.WriteAllBytes(
                Path.Combine(staging, $"{assessment.AssessmentKind}.assessment.json"),
                assessmentBytes);
            File.WriteAllBytes(
                Path.Combine(staging, $"{assessment.AssessmentKind}.evidence.json"),
                evidenceBytes);
            File.WriteAllBytes(
                Path.Combine(staging, $"{assessment.AssessmentKind}.report.md"),
                reportBytes);
            File.WriteAllBytes(
                Path.Combine(staging, $"{assessment.AssessmentKind}.validation.json"),
                manifestBytes);
            BeforePublishForTests?.Invoke();
            InputManifestService.Validate(input, root, requireConfirmed: true);
            inputSnapshot.EnsureUnchanged();
            assessmentSnapshot.EnsureUnchanged();
            evidenceSnapshot.EnsureUnchanged();
            feedbackSnapshot?.EnsureUnchanged();
            packageFeedbackSnapshot?.EnsureUnchanged();
            contextFeedbackSnapshot?.EnsureUnchanged();
            foreach (var snapshot in referencedSnapshots)
            {
                snapshot.EnsureUnchanged();
            }

            if (packageBinding is not null)
            {
                var rebound = RevisionService.LoadPackageBinding(
                    root,
                    options.Optional("--package-revision")!,
                    packageFeedbackSnapshot?.Bytes);
                if (rebound.Reference != packageBinding.Reference)
                {
                    throw new DeterministicValidationException(
                        "Validated package revision changed while the component report was rendered.");
                }
            }
            if (scopedPackageContext is not null)
            {
                var rebound = AssessmentBindingOptions.Load(root, assessment.AssessmentKind, input,
                    options.Optional("--package-revision"), packageFeedbackSnapshot?.Bytes,
                    options.Optional("--package-context-revision"), contextFeedbackSnapshot?.Bytes);
                if (rebound.Context is null ||
                    rebound.Context.ValidationDigest != scopedPackageContext.ValidationDigest ||
                    rebound.Context.ValidationSize != scopedPackageContext.ValidationSize)
                    throw new DeterministicValidationException(
                        "Scoped package context changed while the component report was rendered.");
                AssessmentService.Validate(root, assessment, assessmentBytes, input, inputBytes, evidence,
                    scopedPackageContext: rebound.Context);
            }

            EnsureRevisionRootUnchanged(outputRoot, nextSequence, staging);
            if (predecessor is not null)
            {
                var currentPredecessor = RevisionService.VerifyRevision(
                    root,
                    predecessor.Directory,
                    feedbackBytes: null,
                    packageBinding,
                    validateChain: true,
                    allowMissingFeedback: true,
                    scopedPackageContext);
                if (ContractJson.RawDigest(currentPredecessor.ManifestBytes) != predecessorDigest)
                {
                    throw new DeterministicValidationException(
                        "The immediate predecessor manifest changed while the revision was rendered.");
                }
            }
        });
        return ExitCodes.Success;
    }

    private static int Verify(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count == 1 && IsHelp(args[0]))
        {
            output.WriteLine(Help);
            return ExitCodes.Success;
        }

        var options = CommandOptions.Parse(
            args,
            "--root",
            "--revision",
            "--feedback",
            "--package-revision",
            "--package-feedback",
            "--package-context-revision",
            "--package-context-feedback");
        var root = Path.GetFullPath(options.Single("--root"));
        var revision = Path.GetFullPath(options.Single("--revision"));
        var feedbackBytes = ReadOptional(options.Optional("--feedback"), "assessment feedback");
        var packageFeedbackBytes = ReadOptional(
            options.Optional("--package-feedback"),
            "package assessment feedback");
        var contextFeedbackBytes = ReadOptional(
            options.Optional("--package-context-feedback"), "scoped package context feedback");
        EnsureFeedbackOutsideRevisions(
            options.Optional("--feedback"),
            Path.GetDirectoryName(revision)!);
        if (options.Optional("--package-revision") is { } packageRevision)
        {
            EnsureFeedbackOutsideRevisions(
                options.Optional("--package-feedback"),
                Path.GetDirectoryName(Path.GetFullPath(packageRevision))!);
        }
        if (options.Optional("--package-context-revision") is { } contextRevision)
            EnsureFeedbackOutsideRevisions(options.Optional("--package-context-feedback"),
                Path.GetDirectoryName(SafePath.ResolveExistingDirectoryUnderRoot(root,
                    Path.IsPathRooted(contextRevision) ? contextRevision : Path.Combine(root, contextRevision)))!);

        var kind = RevisionService.DetectKind(root, revision);
        var bindings = AssessmentBindingOptions.Load(
            root,
            kind,
            AssessmentBindingOptions.ReadRevisionInput(root, revision),
            options.Optional("--package-revision"),
            packageFeedbackBytes,
            options.Optional("--package-context-revision"),
            contextFeedbackBytes);
        _ = RevisionService.VerifyRevision(
            root,
            revision,
            feedbackBytes,
            bindings.Package,
            validateChain: true,
            scopedPackageContext: bindings.Context);
        return ExitCodes.Success;
    }

    private static int NextSequence(string outputRoot)
    {
        return NextSequence(outputRoot, ignoredDirectory: null);
    }

    private static int NextSequence(string outputRoot, string? ignoredDirectory)
    {
        if (!Directory.Exists(outputRoot))
        {
            return 1;
        }

        if (Directory.GetFiles(outputRoot, "*", SearchOption.TopDirectoryOnly).Length != 0)
        {
            throw new DeterministicValidationException(
                "Revision root cannot contain mutable files or a latest pointer.");
        }

        var ignored = ignoredDirectory is null ? null : Path.GetFullPath(ignoredDirectory);
        var directories = Directory.GetDirectories(outputRoot)
            .Where(path => ignored is null ||
                !string.Equals(Path.GetFullPath(path), ignored, StringComparison.Ordinal))
            .Select(path => new
            {
                Path = path,
                Name = Path.GetFileName(path)
            })
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
        for (var index = 0; index < directories.Length; index++)
        {
            var expected = (index + 1).ToString("D4");
            if (directories[index].Name != expected)
            {
                throw new DeterministicValidationException(
                    "Revision root contains a fork, reused name, or skipped sequence.");
            }
        }

        return directories.Length + 1;
    }

    private static void EnsureRevisionRootUnchanged(
        string outputRoot,
        int expectedNextSequence,
        string stagingDirectory)
    {
        if (NextSequence(outputRoot, stagingDirectory) != expectedNextSequence)
        {
            throw new DeterministicValidationException(
                "Revision root sequence changed while the revision was rendered.");
        }
    }

    private static IReadOnlyList<string> ParseChangedIds(string? value)
    {
        if (value is null)
        {
            return [];
        }

        var ids = value.Split(',', StringSplitOptions.None)
            .Select(id => ContractJson.NormalizeText(id, "declared changed ID", 64))
            .ToArray();
        if (ids.Any(string.IsNullOrWhiteSpace) ||
            ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
        {
            throw new DeterministicValidationException(
                "--changed-ids must contain unique comma-separated requirement IDs.");
        }

        return ids.Order(StringComparer.Ordinal).ToArray();
    }

    private static Sha256Digest ParseDigest(string value, string resource)
    {
        var digest = new Sha256Digest("sha256", value);
        EvidenceIdentity.ValidateDigest(digest, resource);
        return digest;
    }

    private static ImmutableInputSnapshot? CaptureOptional(string? path, string resource) =>
        path is null ? null : ImmutableInputSnapshot.Capture(Path.GetFullPath(path), resource);

    private static byte[]? ReadOptional(string? path, string resource) =>
        path is null
            ? null
            : BoundedIO.ReadAllBytes(
                Path.GetFullPath(path),
                ResourceLimits.SerializedArtifactBytes,
                resource);

    private static void EnsureFeedbackOutsideRevisions(
        ImmutableInputSnapshot? feedback,
        string revisionsRoot)
    {
        if (feedback is not null)
        {
            EnsureFeedbackOutsideRevisions(feedback.Path, revisionsRoot);
        }
    }

    private static void EnsureFeedbackOutsideRevisions(string? feedbackPath, string revisionsRoot)
    {
        if (feedbackPath is null)
        {
            return;
        }

        var relative = Path.GetRelativePath(
            Path.GetFullPath(revisionsRoot),
            Path.GetFullPath(feedbackPath));
        if (relative == "." ||
            relative != ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative))
        {
            throw new DeterministicValidationException(
                "Assessment feedback must live outside immutable revision directories.");
        }
    }

    private static IReadOnlyList<ImmutableInputSnapshot> CaptureReferencedInputs(
        InputManifest input,
        string root)
    {
        var snapshots = new List<ImmutableInputSnapshot>
        {
            ImmutableInputSnapshot.Capture(
                SafePath.ResolveUnderRoot(
                    root,
                    input.Package.NupkgPath,
                    requireExisting: true,
                    requireFile: true),
                ResourceLimits.NupkgBytes,
                "referenced nupkg")
        };
        snapshots.AddRange(input.Documentation.Select(document =>
            ImmutableInputSnapshot.Capture(
                SafePath.ResolveUnderRoot(
                    root,
                    document.ContentPath,
                    requireExisting: true,
                    requireFile: true),
                ResourceLimits.SupplementalInputAggregateBytes,
                $"referenced documentation '{document.ContentPath}'")));
        snapshots.AddRange(input.PackageSources.Select(source =>
            ImmutableInputSnapshot.Capture(
                SafePath.ResolveUnderRoot(
                    root,
                    source.ContentPath,
                    requireExisting: true,
                    requireFile: true),
                ResourceLimits.SupplementalInputAggregateBytes,
                $"referenced package source '{source.ContentPath}'")));
        snapshots.AddRange(input.SourceArtifacts.Select(artifact =>
            ImmutableInputSnapshot.Capture(
                SafePath.ResolveUnderRoot(
                    root,
                    artifact.ContentPath,
                    requireExisting: true,
                    requireFile: true),
                ResourceLimits.SupplementalInputAggregateBytes,
                $"referenced source artifact '{artifact.ContentPath}'")));
        snapshots.AddRange(input.OwnerInputs.Select(owner =>
            ImmutableInputSnapshot.Capture(
                SafePath.ResolveUnderRoot(
                    root,
                    owner.Basename,
                    requireExisting: true,
                    requireFile: true),
                ResourceLimits.SupplementalInputAggregateBytes,
                $"referenced owner input '{owner.Basename}'")));
        return snapshots
            .GroupBy(snapshot => snapshot.Path, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static bool IsHelp(string value) => value is "--help" or "-h";
}
