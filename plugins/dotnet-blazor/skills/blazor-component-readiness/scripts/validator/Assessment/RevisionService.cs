using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Rendering;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Assessment;

public sealed record RevisionArtifacts(
    string Directory,
    string Kind,
    InputManifest Input,
    byte[] InputBytes,
    ReadinessAssessment Assessment,
    byte[] AssessmentBytes,
    EvidenceBundle Evidence,
    byte[] EvidenceBytes,
    byte[] ReportBytes,
    ValidationManifest Manifest,
    byte[] ManifestBytes);

public sealed record PackageRevisionBinding(
    InputManifest Input,
    ReadinessAssessment Assessment,
    ValidationManifest Manifest,
    PackageAssessmentReference Reference);

public sealed class ScopedPackageContextBinding
{
    internal ScopedPackageContextBinding(RevisionArtifacts revision)
    {
        Input = revision.Input;
        Assessment = revision.Assessment;
        Manifest = revision.Manifest;
        ValidationDigest = ContractJson.RawDigest(revision.ManifestBytes);
        ValidationSize = revision.ManifestBytes.LongLength;
    }

    public InputManifest Input { get; }
    public ReadinessAssessment Assessment { get; }
    public ValidationManifest Manifest { get; }
    public Sha256Digest ValidationDigest { get; }
    public long ValidationSize { get; }
}

public static class RevisionService
{
    public static ScopedPackageContextBinding LoadScopedPackageContextBinding(
        string root, string revisionDirectory, byte[]? feedbackBytes)
    {
        var revision = VerifyRevision(root, revisionDirectory, feedbackBytes, packageBinding: null,
            validateChain: true);
        var scope = AuthorizedPackageScope.Load(root, revision.Input);
        var rubric = RubricLoader.Load();
        var expected = rubric.CoreRequirements
            .Where(row => row.Scope == "repository-wide" && row.Basis is { RequiresPolicyApproval: false })
            .Select(row => row.Id).ToArray();
        if (scope is null || revision.Kind != "package" ||
            revision.Assessment.CompletionState != "complete" || revision.Manifest.CompletionState != "complete" ||
            revision.Assessment.RubricVersion != rubric.RubricVersion ||
            revision.Assessment.RubricDigest != rubric.RubricDigest ||
            revision.Assessment.ScopeSchemaVersion != rubric.ScopeSchemaVersion ||
            revision.Assessment.ScopeMapDigest != rubric.ScopeMapDigest ||
            revision.Assessment.Overlays.Count != 0 ||
            !revision.Assessment.SelectedIds.SequenceEqual(expected, StringComparer.Ordinal))
            throw new DeterministicValidationException(
                "Scoped package context requires a complete, verified requirement-backed package scope and its full declared validation closure, not an ordinary package prerequisite.");
        return new ScopedPackageContextBinding(revision);
    }

    public static PackageRevisionBinding LoadPackageBinding(
        string root,
        string revisionDirectory,
        byte[]? feedbackBytes)
    {
        var revision = VerifyRevision(
            root,
            revisionDirectory,
            feedbackBytes,
            packageBinding: null,
            validateChain: true,
            allowMissingFeedback: true);
        if (revision.Kind != "package" ||
            revision.Assessment.CompletionState != "complete" ||
            revision.Manifest.CompletionState != "complete" ||
            AuthorizedPackageScope.IsRequested(revision.Input))
        {
            throw new DeterministicValidationException(
                "Package binding requires a complete validated package assessment revision.");
        }

        var reference = new PackageAssessmentReference(
            revision.Assessment.Identity.Package,
            revision.Manifest.InputManifestDigest,
            revision.Manifest.AssessmentDigest,
            revision.Manifest.ReportDigest,
            ContractJson.RawDigest(revision.ManifestBytes));
        return new PackageRevisionBinding(
            revision.Input,
            revision.Assessment,
            revision.Manifest,
            reference);
    }

    public static RevisionArtifacts VerifyRevision(
        string root,
        string revisionDirectory,
        byte[]? feedbackBytes,
        PackageRevisionBinding? packageBinding,
        bool validateChain,
        bool allowMissingFeedback = false,
        ScopedPackageContextBinding? scopedPackageContext = null) =>
        VerifyRevisionCore(root, revisionDirectory, feedbackBytes, packageBinding, validateChain,
            allowMissingFeedback, scopedPackageContext, feedbackBytes);

    private static RevisionArtifacts VerifyRevisionCore(
        string root,
        string revisionDirectory,
        byte[]? feedbackBytes,
        PackageRevisionBinding? packageBinding,
        bool validateChain,
        bool allowMissingFeedback,
        ScopedPackageContextBinding? scopedPackageContext,
        byte[]? availableFeedbackBytes)
    {
        var revision = SafePath.ResolveExistingDirectoryUnderRoot(root, revisionDirectory);
        var sequence = ParseSequence(revision);
        var validationFiles = Directory.GetFiles(
            revision,
            "*.validation.json",
            SearchOption.TopDirectoryOnly);
        foreach (var validationFile in validationFiles)
        {
            _ = SafePath.ResolveUnderRoot(
                revision,
                Path.GetFileName(validationFile),
                requireExisting: true,
                requireFile: true);
        }
        if (validationFiles.Length != 1)
        {
            throw new DeterministicValidationException(
                "Revision requires exactly one validation manifest.");
        }

        var kind = Path.GetFileName(validationFiles[0]).Split('.')[0];
        if (kind is not ("unified" or "package" or "component"))
        {
            throw new DeterministicValidationException("Revision validation filename has an invalid assessment kind.");
        }

        var expectedFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "input-manifest.json",
            $"{kind}.assessment.json",
            $"{kind}.evidence.json",
            $"{kind}.report.md",
            $"{kind}.validation.json"
        };
        var actualFiles = Directory.GetFiles(revision, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.Ordinal);
        if (!actualFiles.SetEquals(expectedFiles) ||
            Directory.GetDirectories(revision, "*", SearchOption.TopDirectoryOnly).Length != 0)
        {
            throw new DeterministicValidationException(
                "Immutable revisions contain exactly the input, assessment, evidence, report, and validation artifacts.");
        }

        var inputBytes = Read(revision, "input-manifest.json", "input manifest snapshot");
        var assessmentBytes = Read(
            revision,
            $"{kind}.assessment.json",
            "assessment snapshot");
        var evidenceBytes = Read(
            revision,
            $"{kind}.evidence.json",
            "evidence snapshot");
        var reportBytes = Read(revision, $"{kind}.report.md", "report");
        var manifestBytes = Read(revision, $"{kind}.validation.json", "validation manifest");
        var input = InputManifestService.Parse(inputBytes);
        InputManifestService.Validate(input, root, requireConfirmed: true);
        var assessment = AssessmentService.Parse(assessmentBytes);
        var evidence = CanonicalEvidenceJson.ParseBundle(evidenceBytes);
        var manifest = ReportService.ParseManifest(manifestBytes);
        AssessmentService.Validate(
            root,
            assessment,
            assessmentBytes,
            input,
            inputBytes,
            evidence,
            packageBinding,
            scopedPackageContext);
        if (assessment.AssessmentKind != kind || manifest.AssessmentKind != kind)
        {
            throw new DeterministicValidationException(
                "Revision filenames, assessment kind, and validation manifest kind must match.");
        }

        AssessmentFeedback? feedback = null;
        if (manifest.FeedbackDigest is null)
        {
            if (feedbackBytes is not null)
            {
                throw new DeterministicValidationException(
                    "Revision does not bind an assessment feedback file.");
            }
        }
        else if (feedbackBytes is null)
        {
            if (!allowMissingFeedback || AuthorizedPackageScope.IsRequested(input) ||
                ScopedComponentProfile.IsRequested(input))
            {
                throw new DeterministicValidationException(
                    ScopedComponentProfile.IsRequested(input)
                        ? $"Scoped-component revision '{Path.GetFileName(revision)}' requires its exact bound assessment feedback " +
                            $"(SHA-256 '{manifest.FeedbackDigest.Value}'); missing or different historical feedback cannot be skipped."
                        : "Revision verification requires the exact bound assessment feedback file.");
            }
        }
        else
        {
            feedback = FeedbackService.Parse(
                feedbackBytes,
                assessment,
                packageBinding?.Assessment.SelectedIds);
            if (feedback.Digest != manifest.FeedbackDigest)
            {
                throw new DeterministicValidationException(
                    "Assessment feedback bytes differ from the validation-manifest digest.");
            }
        }

        if (ContractJson.RawDigest(reportBytes) != manifest.ReportDigest)
        {
            throw new DeterministicValidationException(
                "Rendered report bytes differ from the validation-manifest digest.");
        }

        if (manifest.FeedbackDigest is null || feedback is not null)
        {
            var expectedReport = ReportService.RenderMarkdown(
                assessment, input, evidence, feedback, root, scopedPackageContext);
            if (!reportBytes.AsSpan().SequenceEqual(expectedReport))
            {
                throw new DeterministicValidationException(
                    "Rendered report differs by one or more bytes or contains extra bytes.");
            }
        }

        var expectedManifest = ReportService.CreateManifest(
            assessment,
            assessmentBytes,
            input,
            inputBytes,
            evidence,
            evidenceBytes,
            reportBytes,
            manifest.PredecessorManifestDigest,
            manifest.FeedbackDigest,
            manifest.DeclaredChangedIds,
            root,
            scopedPackageContext);
        ReportService.ValidateManifest(manifest, expectedManifest);

        if (sequence == 1)
        {
            if (manifest.PredecessorManifestDigest is not null ||
                manifest.DeclaredChangedIds.Count != 0)
            {
                throw new DeterministicValidationException(
                    "Revision 0001 cannot declare a predecessor or changed requirement IDs.");
            }
        }
        else if (manifest.PredecessorManifestDigest is null)
        {
            throw new DeterministicValidationException(
                "Every revision after 0001 requires the immediate predecessor manifest digest.");
        }

        var artifacts = new RevisionArtifacts(
            revision,
            kind,
            input,
            inputBytes,
            assessment,
            assessmentBytes,
            evidence,
            evidenceBytes,
            reportBytes,
            manifest,
            manifestBytes);
        if (!validateChain || sequence == 1)
        {
            return artifacts;
        }

        var predecessorDirectory = Path.Combine(
            Path.GetDirectoryName(revision)!,
            (sequence - 1).ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
        if (!Directory.Exists(predecessorDirectory))
        {
            throw new DeterministicValidationException(
                "Revision chain has a missing or skipped immediate predecessor.");
        }

        byte[]? predecessorFeedback = null;
        if (ScopedComponentProfile.IsRequested(input) && availableFeedbackBytes is not null)
        {
            var predecessorPath = SafePath.ResolveExistingDirectoryUnderRoot(root, predecessorDirectory);
            var predecessorManifest = ReportService.ParseManifest(Read(
                predecessorPath, $"{kind}.validation.json", "predecessor validation manifest"));
            if (predecessorManifest.FeedbackDigest == ContractJson.RawDigest(availableFeedbackBytes))
                predecessorFeedback = availableFeedbackBytes;
        }

        var predecessor = VerifyRevisionCore(
            root,
            predecessorDirectory,
            predecessorFeedback,
            packageBinding,
            validateChain: true,
            allowMissingFeedback: true,
            scopedPackageContext,
            availableFeedbackBytes);
        if (ContractJson.RawDigest(predecessor.ManifestBytes) != manifest.PredecessorManifestDigest)
        {
            throw new DeterministicValidationException(
                "Revision predecessor digest does not name the immediate preceding manifest.");
        }

        ValidateChainIdentity(predecessor, artifacts);
        if (manifest.DeclaredChangedIds.Count == 0)
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
            ValidateCorrection(predecessor, artifacts, manifest.DeclaredChangedIds);
        }

        return artifacts;
    }

    public static void ValidateCorrection(
        RevisionArtifacts predecessor,
        RevisionArtifacts replacement,
        IReadOnlyList<string> declaredChangedIds)
    {
        var declared = declaredChangedIds
            .Select(id => ContractJson.NormalizeText(id, "declared changed ID", 64))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (declared.Length == 0 ||
            !declared.SequenceEqual(declaredChangedIds, StringComparer.Ordinal) ||
            declared.Any(id => !replacement.Assessment.SelectedIds.Contains(id, StringComparer.Ordinal)))
        {
            throw new DeterministicValidationException(
                "Evidence-backed corrections require nonempty, selected, unique, canonically sorted changed IDs.");
        }

        ValidateChainIdentity(predecessor, replacement);
        if (!predecessor.InputBytes.AsSpan().SequenceEqual(replacement.InputBytes))
        {
            throw new DeterministicValidationException(
                "Assessment corrections cannot change confirmed input-manifest bytes.");
        }

        if (predecessor.Manifest.FeedbackDigest != replacement.Manifest.FeedbackDigest)
        {
            throw new DeterministicValidationException(
                "Evidence-backed corrections must preserve the predecessor feedback digest exactly; feedback edits require a separate feedback-only revision.");
        }

        var actual = predecessor.Assessment.Rows.Zip(
                replacement.Assessment.Rows,
                (before, after) => new
                {
                    before.Id,
                    Changed = !AssessmentService.SerializeRow(before)
                        .AsSpan()
                        .SequenceEqual(AssessmentService.SerializeRow(after))
                })
            .Where(item => item.Changed)
            .Select(item => item.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (actual.Length == 0 || !actual.SequenceEqual(declared, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                "Declared changed IDs must exactly equal the actual canonical assessment-row changes.");
        }

        var beforeRows = predecessor.Assessment.Rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var afterRows = replacement.Assessment.Rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var predecessorSelectedEvidence = predecessor.Manifest.SelectedEvidenceIds
            .ToHashSet(StringComparer.Ordinal);
        foreach (var id in declared)
        {
            var before = beforeRows[id];
            var after = afterRows[id];
            if ((before.Status != after.Status || AuthorizedPackageScope.IsRequested(predecessor.Input) ||
                 ScopedComponentProfile.IsRequested(predecessor.Input)) &&
                !after.EvidenceIds.Except(predecessorSelectedEvidence, StringComparer.Ordinal).Any())
            {
                var changeKind = AuthorizedPackageScope.IsRequested(predecessor.Input) ? "Authorized-scope finding" :
                    ScopedComponentProfile.IsRequested(predecessor.Input) ? "Scoped finding" : "Status";
                throw new DeterministicValidationException(
                    $"{changeKind} change for '{id}' requires evidence absent from the predecessor's entire selected-evidence set.");
            }
        }

        var beforeRecords = Records(predecessor.Evidence);
        var afterRecords = Records(replacement.Evidence);
        foreach (var selection in predecessor.Evidence.Selection)
        {
            if (!replacement.Evidence.Selection.Any(
                    item => item.EvidenceId == selection.EvidenceId) ||
                !afterRecords.TryGetValue(selection.EvidenceId, out var afterRecord) ||
                !SameRecord(beforeRecords[selection.EvidenceId], afterRecord))
            {
                throw new DeterministicValidationException(
                    "Assessment corrections cannot remove or mutate previously selected evidence.");
            }
        }
    }

    public static int ParseSequence(string revisionDirectory)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(revisionDirectory));
        if (name.Length != 4 ||
            !int.TryParse(
                name,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var sequence) ||
            sequence < 1)
        {
            throw new DeterministicValidationException(
                "Revision directory names must be positive zero-padded four-digit sequences.");
        }

        return sequence;
    }

    private static void ValidateChainIdentity(
        RevisionArtifacts predecessor,
        RevisionArtifacts replacement)
    {
        var before = predecessor.Assessment;
        var after = replacement.Assessment;
        if (after.SchemaVersion < before.SchemaVersion ||
            after.SchemaVersion - before.SchemaVersion > 1)
        {
            throw new DeterministicValidationException(
                "Revision chains cannot downgrade or skip assessment schema generations.");
        }

        if (predecessor.Kind != replacement.Kind ||
            before.Identity != after.Identity ||
            before.RubricVersion != after.RubricVersion ||
            before.ScopeSchemaVersion != after.ScopeSchemaVersion ||
            before.RubricDigest != after.RubricDigest ||
            before.ScopeMapDigest != after.ScopeMapDigest ||
            !before.Overlays.SequenceEqual(after.Overlays) ||
            !before.SelectedIds.SequenceEqual(after.SelectedIds, StringComparer.Ordinal) ||
            before.PackageReference != after.PackageReference)
        {
            throw new DeterministicValidationException(
                "Revision chain assessment identity, package, rubric, scope, or overlays drifted.");
        }
    }

    private static Dictionary<string, EvidenceRecord> Records(EvidenceBundle bundle) =>
        bundle.SourceLedgers.SelectMany(source => source.Ledger.Records)
            .ToDictionary(record => record.StableId, StringComparer.Ordinal);

    private static bool SameRecord(EvidenceRecord before, EvidenceRecord after) =>
        before.StableId == after.StableId &&
        before.Claim == after.Claim &&
        before.Applicability == after.Applicability &&
        before.Provenance == after.Provenance &&
        before.Supersedes.SequenceEqual(after.Supersedes, StringComparer.Ordinal);

    public static string DetectKind(string root, string revisionDirectory)
    {
        var revision = SafePath.ResolveExistingDirectoryUnderRoot(root, revisionDirectory);
        var files = Directory.GetFiles(revision, "*.validation.json", SearchOption.TopDirectoryOnly);
        if (files.Length != 1)
        {
            throw new DeterministicValidationException(
                "Revision requires exactly one validation manifest.");
        }

        var file = SafePath.ResolveUnderRoot(
            revision,
            Path.GetFileName(files[0]),
            requireExisting: true,
            requireFile: true);
        return Path.GetFileName(file).Split('.')[0];
    }

    private static byte[] Read(string revision, string filename, string resource)
    {
        var path = SafePath.ResolveUnderRoot(
            revision,
            filename,
            requireExisting: true,
            requireFile: true);
        SafePath.EnsureRegularFile(path);
        return BoundedIO.ReadAllBytes(path, ResourceLimits.SerializedArtifactBytes, resource);
    }
}
