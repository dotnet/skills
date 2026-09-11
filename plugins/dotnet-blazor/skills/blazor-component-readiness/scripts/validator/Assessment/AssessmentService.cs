using System.Text.Json;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Assessment;

public static class AssessmentService
{
    public const int SchemaVersion = 2;
    public const int LegacySchemaVersion = 1;
    public static readonly IReadOnlyList<string> CompletionStates = ["incomplete", "targeted", "complete"];

    public static ReadinessAssessment Initialize(
        string kind,
        string root,
        InputManifest input,
        ReadOnlySpan<byte> inputBytes,
        string? componentId,
        IReadOnlyList<string> overlayIds,
        PackageRevisionBinding? packageBinding = null,
        string? rubricVersion = null,
        ScopedPackageContextBinding? scopedPackageContext = null)
    {
        InputManifestService.Validate(input, root, requireConfirmed: true);
        var profile = ScopedComponentProfile.Load(root, input);
        kind = kind.ToLowerInvariant();
        string? canonicalComponent = null;
        if (kind is "unified" or "component")
        {
            if (componentId is null)
            {
                throw new DeterministicValidationException($"{kind} assessment requires --component.");
            }

            canonicalComponent = Canonicalization.ComponentId(componentId);
            if (!input.Components.Any(component => component.Id == canonicalComponent))
            {
                throw new DeterministicValidationException(
                    $"Component '{canonicalComponent}' is absent from the confirmed input manifest.");
            }
        }
        else if (kind != "package" || componentId is not null)
        {
            throw new DeterministicValidationException("Assessment kind must be unified, package, or component.");
        }

        if (profile is null && scopedPackageContext is not null)
            throw new DeterministicValidationException("Scoped package context requires the explicit Scoped-component profile.");

        if (kind == "component" && packageBinding is null && profile is null)
        {
            throw new DeterministicValidationException(
                "Component assessment requires an exact validated package revision.");
        }

        if (kind != "component" && packageBinding is not null)
        {
            throw new DeterministicValidationException(
                "Only component assessments may bind a package revision.");
        }

        if (kind is "package" or "component" && overlayIds.Count != 0)
        {
            throw new DeterministicValidationException(
                "Split package/component assessments contain their version's canonical rows and cannot add selected overlays.");
        }

        var rubric = RubricLoader.Load(rubricVersion);
        var authorizedScope = AuthorizedPackageScope.Load(root, input);
        var requirements = profile?.Select(rubric, kind) ??
            authorizedScope?.Select(rubric, kind) ?? RubricLoader.Select(rubric, kind, overlayIds);
        var overlays = rubric.Overlays
            .Where(overlay => overlayIds.Contains(overlay.Id, StringComparer.Ordinal))
            .Select(overlay => new AssessmentOverlay(overlay.Id, overlay.Version, overlay.Digest))
            .ToArray();
        var package = new EvidencePackageIdentity(
            input.Package.PackageId,
            input.Package.Version,
            input.Package.NupkgDigest);
        var identity = new ExactAssessmentIdentity(
            kind,
            package,
            InputManifestService.Digest(inputBytes),
            canonicalComponent);
        var assessment = new ReadinessAssessment(
            SchemaVersion,
            kind,
            identity,
            rubric.RubricVersion,
            rubric.ScopeSchemaVersion,
            rubric.RubricDigest,
            rubric.ScopeMapDigest,
            overlays,
            requirements.Select(requirement => requirement.Id).ToArray(),
            packageBinding?.Reference,
            requirements.Select(requirement => new AssessmentRow(
                requirement.Id,
                requirement.Requirement,
                requirement.Scope,
                requirement.Area,
                null,
                null,
                [],
                null,
                null,
                null)).ToArray(),
            [],
            [],
            "incomplete");
        if (packageBinding is not null)
        {
            ValidatePackageBinding(assessment, input, packageBinding);
        }
        profile?.ValidateBinding(assessment, input, scopedPackageContext, packageBinding);

        return assessment;
    }

    public static void ValidateInputIdentity(
        ExactAssessmentIdentity identity,
        InputManifest input,
        ReadOnlySpan<byte> inputBytes)
    {
        EvidenceIdentity.ValidateAssessment(identity);
        if (input.State != "confirmed")
        {
            throw new DeterministicValidationException("A draft input manifest cannot be used for assessment.");
        }

        var expectedInputDigest = InputManifestService.Digest(inputBytes);
        var expectedPackage = new EvidencePackageIdentity(
            input.Package.PackageId,
            input.Package.Version,
            input.Package.NupkgDigest);
        if (identity.InputManifestDigest != expectedInputDigest ||
            identity.Package != expectedPackage)
        {
            throw new DeterministicValidationException(
                "Assessment identity must match the confirmed input-manifest digest and package identity exactly. " +
                "Initialize/export identity from the final confirmed manifest and rebuild downstream ledgers and bundle.");
        }

        if (identity.AssessmentKind is "unified" or "component")
        {
            if (identity.ComponentId is null ||
                !input.Components.Any(component => component.Id == identity.ComponentId))
            {
                throw new DeterministicValidationException(
                    "Assessment component must be present in the confirmed input manifest.");
            }
        }

        if (identity.AssessmentKind == "package" && identity.ComponentId is not null)
        {
            throw new DeterministicValidationException("Package assessments cannot identify a component.");
        }
    }

    public static void Validate(
        string root,
        ReadinessAssessment assessment,
        ReadOnlySpan<byte> assessmentBytes,
        InputManifest input,
        ReadOnlySpan<byte> inputBytes,
        EvidenceBundle evidence,
        PackageRevisionBinding? packageBinding = null,
        ScopedPackageContextBinding? scopedPackageContext = null)
    {
        if (assessment.SchemaVersion is not (LegacySchemaVersion or SchemaVersion) ||
            assessment.AssessmentKind is not ("unified" or "package" or "component") ||
            assessment.AssessmentKind != assessment.Identity.AssessmentKind)
        {
            throw new DeterministicValidationException("Assessment schema or kind is invalid.");
        }

        ValidateInputIdentity(assessment.Identity, input, inputBytes);
        if (evidence.Assessment != assessment.Identity)
        {
            throw new DeterministicValidationException(
                "Assessment, input-manifest digest, package identity, component, and evidence identity must match exactly.");
        }
        var expectedPackage = assessment.Identity.Package;

        var profile = ScopedComponentProfile.Load(root, input);
        if (profile is not null)
            profile.ValidateBinding(assessment, input, scopedPackageContext, packageBinding);
        else
        {
            if (scopedPackageContext is not null)
                throw new DeterministicValidationException("Ordinary assessments cannot use scoped package context.");
            ValidatePackageReference(assessment, expectedPackage, input, packageBinding);
        }
        var rubric = RubricLoader.Load(assessment.RubricVersion);
        if (rubric.RubricVersion == RubricLoader.CurrentVersion &&
            assessment.SchemaVersion != SchemaVersion)
        {
            throw new DeterministicValidationException(
                "The current normative rubric requires the current assessment evidence protocols.");
        }

        var selectedOverlayIds = assessment.Overlays.Select(overlay => overlay.Id).ToArray();
        if (assessment.AssessmentKind is "package" or "component" &&
            selectedOverlayIds.Length != 0)
        {
            throw new DeterministicValidationException(
                "Split package/component assessments cannot contain overlay rows.");
        }

        var authorizedScope = AuthorizedPackageScope.Load(root, input);
        var expectedRequirements = profile?.Select(rubric, assessment.AssessmentKind) ??
            authorizedScope?.Select(rubric, assessment.AssessmentKind) ??
            RubricLoader.Select(rubric, assessment.AssessmentKind, selectedOverlayIds);
        var expectedOverlays = rubric.Overlays
            .Where(overlay => selectedOverlayIds.Contains(overlay.Id, StringComparer.Ordinal))
            .Select(overlay => new AssessmentOverlay(overlay.Id, overlay.Version, overlay.Digest))
            .ToArray();
        if (assessment.RubricVersion != rubric.RubricVersion ||
            assessment.ScopeSchemaVersion != rubric.ScopeSchemaVersion ||
            assessment.RubricDigest != rubric.RubricDigest ||
            assessment.ScopeMapDigest != rubric.ScopeMapDigest ||
            !assessment.Overlays.SequenceEqual(expectedOverlays) ||
            !assessment.SelectedIds.SequenceEqual(expectedRequirements.Select(row => row.Id), StringComparer.Ordinal) ||
            assessment.Rows.Count != expectedRequirements.Count)
        {
            throw new DeterministicValidationException(
                "Assessment rubric, scope, overlay, or selected-ID binding is invalid.");
        }

        for (var index = 0; index < assessment.Rows.Count; index++)
        {
            var row = assessment.Rows[index];
            var expected = expectedRequirements[index];
            if (row.Id != expected.Id ||
                row.Requirement != expected.Requirement ||
                row.Scope != expected.Scope ||
                row.Area != expected.Area)
            {
                throw new DeterministicValidationException(
                    $"Assessment row {index + 1} is missing, duplicated, reordered, unknown, or has wording/scope drift.");
            }
        }

        var selectedEvidence = evidence.Selection
            .OrderBy(selection => selection.DisplayOrder)
            .Select(selection => selection.EvidenceId)
            .ToArray();
        if (selectedEvidence.Distinct(StringComparer.Ordinal).Count() != selectedEvidence.Length)
        {
            throw new DeterministicValidationException("Evidence selection contains duplicate identifiers.");
        }

        var selectedSet = selectedEvidence.ToHashSet(StringComparer.Ordinal);
        var applicability = evidence.SourceLedgers
            .SelectMany(ledger => ledger.Ledger.Records)
            .Where(record => selectedSet.Contains(record.StableId))
            .ToDictionary(record => record.StableId, record => record.Applicability, StringComparer.Ordinal);
        var usedEvidence = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in assessment.Rows)
        {
            ValidateRow(row, rubric.Statuses, selectedSet, applicability);
            usedEvidence.UnionWith(row.EvidenceIds);
        }

        ValidateFindings(
            assessment.Findings,
            assessment.Rows,
            assessment.SelectedIds,
            selectedSet,
            usedEvidence);
        ValidateSummaryGroups(
            assessment,
            assessment.SummaryGroups,
            assessment.SelectedIds,
            selectedSet,
            usedEvidence);
        if (!usedEvidence.SetEquals(selectedSet))
        {
            throw new DeterministicValidationException(
                "Every selected evidence ID must be used and no unselected evidence may be referenced.");
        }

        authorizedScope?.Validate(assessment, input, evidence);
        profile?.ValidateEvidence(input, evidence);
        EvidenceInputBindingValidator.Validate(root, assessment, input, evidence);
        EvidenceProtocolValidator.Validate(
            root,
            assessment,
            input,
            evidence,
            enforceCurrentProtocols: assessment.SchemaVersion == SchemaVersion,
            enforceAutoTransitionProtocol: rubric.RubricVersion == RubricLoader.CurrentVersion);
        ValidateCompletion(assessment);
        BoundedIO.EnsureLength(assessmentBytes.Length, ResourceLimits.SerializedArtifactBytes, "assessment");
    }

    public static byte[] Serialize(ReadinessAssessment assessment)
    {
        var bytes = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", assessment.SchemaVersion);
            writer.WriteString("assessment_kind", assessment.AssessmentKind);
            writer.WritePropertyName("identity");
            WriteIdentity(writer, assessment.Identity);
            writer.WriteString("rubric_version", assessment.RubricVersion);
            writer.WriteNumber("scope_schema_version", assessment.ScopeSchemaVersion);
            ContractJson.WriteDigest(writer, "rubric_sha256", assessment.RubricDigest);
            ContractJson.WriteDigest(writer, "scope_map_sha256", assessment.ScopeMapDigest);
            writer.WritePropertyName("overlays");
            writer.WriteStartArray();
            foreach (var overlay in assessment.Overlays)
            {
                WriteOverlay(writer, overlay);
            }

            writer.WriteEndArray();
            writer.WritePropertyName("selected_ids");
            WriteStrings(writer, assessment.SelectedIds);
            writer.WritePropertyName("package_reference");
            WritePackageReference(writer, assessment.PackageReference);
            writer.WritePropertyName("rows");
            writer.WriteStartArray();
            foreach (var row in assessment.Rows)
            {
                WriteRow(writer, row);
            }

            writer.WriteEndArray();
            writer.WritePropertyName("findings");
            writer.WriteStartArray();
            foreach (var finding in assessment.Findings)
            {
                writer.WriteStartObject();
                writer.WriteString("title", finding.Title);
                writer.WriteString("factual_summary", finding.FactualSummary);
                writer.WritePropertyName("requirement_ids");
                WriteStrings(writer, finding.RequirementIds);
                writer.WritePropertyName("evidence_ids");
                WriteStrings(writer, finding.EvidenceIds);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("summary_groups");
            writer.WriteStartArray();
            foreach (var summary in assessment.SummaryGroups)
            {
                writer.WriteStartObject();
                writer.WriteString("name", summary.Name);
                writer.WriteString("factual_summary", summary.FactualSummary);
                writer.WritePropertyName("requirement_ids");
                WriteStrings(writer, summary.RequirementIds);
                writer.WritePropertyName("evidence_ids");
                WriteStrings(writer, summary.EvidenceIds);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteString("completion_state", assessment.CompletionState);
            writer.WriteEndObject();
        });
        BoundedIO.EnsureLength(bytes.Length, ResourceLimits.SerializedArtifactBytes, "assessment");
        return bytes;
    }

    public static byte[] SerializeRow(AssessmentRow row) =>
        StrictJson.SerializeCanonical(writer => WriteRow(writer, row));

    public static ReadinessAssessment Parse(ReadOnlyMemory<byte> bytes, bool requireCanonical = true)
    {
        using var document = StrictJson.Parse(bytes, ResourceLimits.SerializedArtifactBytes, "assessment");
        var root = document.RootElement;
        RequireAssessmentProperties(
            root,
            requireCanonical,
            "schema_version",
            "assessment_kind",
            "identity",
            "rubric_version",
            "scope_schema_version",
            "rubric_sha256",
            "scope_map_sha256",
            "overlays",
            "selected_ids",
            "package_reference",
            "rows",
            "findings",
            "summary_groups",
            "completion_state");
        var assessment = new ReadinessAssessment(
            ContractJson.Int32(root, "schema_version"),
            ContractJson.String(root, "assessment_kind"),
            ParseIdentity(ContractJson.Object(root, "identity"), requireCanonical),
            ContractJson.String(root, "rubric_version"),
            ContractJson.Int32(root, "scope_schema_version"),
            ParseAssessmentDigest(ContractJson.Object(root, "rubric_sha256"), requireCanonical),
            ParseAssessmentDigest(ContractJson.Object(root, "scope_map_sha256"), requireCanonical),
            ContractJson.Array(root, "overlays").EnumerateArray().Select(element => ParseOverlay(element, requireCanonical)).ToArray(),
            ContractJson.StringArray(root, "selected_ids"),
            ParsePackageReference(ContractJson.NullableObject(root, "package_reference"), requireCanonical),
            ContractJson.Array(root, "rows").EnumerateArray().Select(element => ParseRow(element, requireCanonical)).ToArray(),
            ContractJson.Array(root, "findings").EnumerateArray().Select(element => ParseFinding(element, requireCanonical)).ToArray(),
            ContractJson.Array(root, "summary_groups").EnumerateArray().Select(element => ParseSummary(element, requireCanonical)).ToArray(),
            ContractJson.String(root, "completion_state"));
        if (requireCanonical)
        {
            ContractJson.RequireCanonical(bytes.Span, Serialize(assessment), "assessment");
        }

        return assessment;
    }

    private static void ValidatePackageReference(
        ReadinessAssessment assessment,
        EvidencePackageIdentity package,
        InputManifest input,
        PackageRevisionBinding? packageBinding)
    {
        if (assessment.AssessmentKind == "component")
        {
            if (assessment.PackageReference is null ||
                assessment.PackageReference.Package != package ||
                packageBinding is null)
            {
                throw new DeterministicValidationException(
                    "Component assessment requires an exact validated package revision.");
            }

            if (assessment.CompletionState == "complete" &&
                (assessment.PackageReference.AssessmentDigest is null ||
                 assessment.PackageReference.ReportDigest is null))
            {
                throw new DeterministicValidationException(
                    "A complete component assessment requires package assessment and report digests.");
            }

            ValidatePackageBinding(assessment, input, packageBinding);
        }
        else if (assessment.PackageReference is not null || packageBinding is not null)
        {
            throw new DeterministicValidationException(
                "Only component assessments may contain a package reference.");
        }
    }

    private static void ValidatePackageBinding(
        ReadinessAssessment assessment,
        InputManifest componentInput,
        PackageRevisionBinding packageBinding)
    {
        if (packageBinding.Assessment.AssessmentKind != "package" ||
            packageBinding.Assessment.CompletionState != "complete" ||
            packageBinding.Manifest.AssessmentKind != "package" ||
            packageBinding.Manifest.CompletionState != "complete" ||
            assessment.PackageReference != packageBinding.Reference ||
            assessment.Identity.Package != packageBinding.Assessment.Identity.Package ||
            componentInput.Package.PackageId != packageBinding.Input.Package.PackageId ||
            componentInput.Package.Version != packageBinding.Input.Package.Version ||
            componentInput.Package.NupkgDigest != packageBinding.Input.Package.NupkgDigest ||
            componentInput.Source != packageBinding.Input.Source ||
            assessment.RubricVersion != packageBinding.Assessment.RubricVersion ||
            assessment.ScopeSchemaVersion != packageBinding.Assessment.ScopeSchemaVersion ||
            assessment.RubricDigest != packageBinding.Assessment.RubricDigest ||
            assessment.ScopeMapDigest != packageBinding.Assessment.ScopeMapDigest ||
            !assessment.Overlays.SequenceEqual(packageBinding.Assessment.Overlays))
        {
            throw new DeterministicValidationException(
                "Component assessment package ID, version, nupkg digest, source mapping, rubric, scope, overlays, completion, or package artifact binding differs from the validated package revision.");
        }
    }

    private static void ValidateRow(
        AssessmentRow row,
        IReadOnlyList<string> statuses,
        IReadOnlySet<string> selectedEvidence,
        IReadOnlyDictionary<string, EvidenceApplicability> applicability)
    {
        var evidenceIds = row.EvidenceIds.ToArray();
        if (evidenceIds.Distinct(StringComparer.Ordinal).Count() != evidenceIds.Length ||
            !evidenceIds.SequenceEqual(evidenceIds.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            evidenceIds.Any(id => !selectedEvidence.Contains(id)))
        {
            throw new DeterministicValidationException(
                $"Row '{row.Id}' evidence IDs must be selected, unique, and sorted.");
        }

        if (row.Scope == "repository-wide" &&
            row.EvidenceIds.Any(id => applicability[id].Scope != "repository-wide"))
        {
            throw new DeterministicValidationException(
                $"Library-level row '{row.Id}' cannot be established by component-specific evidence.");
        }

        ValidateOptionalField(row.Observation, $"{row.Id} observation");
        ValidateOptionalField(row.OwnerAction, $"{row.Id} owner_action");
        ValidateOptionalField(row.AssessmentFollowUp, $"{row.Id} assessment_follow_up");
        ValidateOptionalField(row.NotApplicableRationale, $"{row.Id} not_applicable_rationale");
        if (row.Status is null)
        {
            if (row.Observation is not null || row.OwnerAction is not null ||
                row.AssessmentFollowUp is not null || row.NotApplicableRationale is not null ||
                row.EvidenceIds.Count > 0)
            {
                throw new DeterministicValidationException(
                    $"Incomplete row '{row.Id}' cannot contain assessment conclusions.");
            }

            return;
        }

        if (!statuses.Contains(row.Status, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException($"Row '{row.Id}' has invalid status '{row.Status}'.");
        }

        switch (row.Status)
        {
            case "verified":
                RequireEvidenceObservation(row, "verified");
                break;
            case "gap":
                RequireEvidenceObservation(row, "gap");
                break;
            case "owner evidence required":
                RequireSubstantive(row.Observation, $"{row.Id} requested owner evidence");
                RequireSubstantive(row.OwnerAction, $"{row.Id} owner_action");
                break;
            case "not tested":
                RequireSubstantive(row.AssessmentFollowUp, $"{row.Id} assessment_follow_up");
                break;
            case "not applicable":
                RequireSubstantive(row.NotApplicableRationale, $"{row.Id} not-applicable rationale");
                break;
        }
    }

    private static void RequireEvidenceObservation(AssessmentRow row, string status)
    {
        if (row.EvidenceIds.Count == 0)
        {
            throw new DeterministicValidationException(
                $"Row '{row.Id}' status '{status}' requires selected evidence.");
        }

        RequireSubstantive(row.Observation, $"{row.Id} factual observation");
    }

    private static void ValidateFindings(
        IReadOnlyList<AssessmentFinding> findings,
        IReadOnlyList<AssessmentRow> rows,
        IReadOnlyList<string> selectedIds,
        IReadOnlySet<string> selectedEvidence,
        ISet<string> usedEvidence)
    {
        foreach (var finding in findings)
        {
            RequireSubstantive(finding.Title, "finding title");
            RequireSubstantive(finding.FactualSummary, "finding factual summary");
            ValidateReferences(finding.RequirementIds, selectedIds, "finding requirement");
            ValidateEvidenceReferences(finding.EvidenceIds, selectedEvidence, "finding evidence");
            ValidateExactEvidenceUnion(
                finding.RequirementIds,
                finding.EvidenceIds,
                rows,
                "finding");
            usedEvidence.UnionWith(finding.EvidenceIds);
        }
    }

    private static void ValidateSummaryGroups(
        ReadinessAssessment assessment,
        IReadOnlyList<AssessmentSummaryGroup> summaries,
        IReadOnlyList<string> selectedIds,
        IReadOnlySet<string> selectedEvidence,
        ISet<string> usedEvidence)
    {
        if (assessment.AssessmentKind == "package" && assessment.CompletionState == "complete")
        {
            var statuses = RubricLoader.Load(assessment.RubricVersion).Statuses;
            var expected = statuses
                .Select(status => new
                {
                    Status = status,
                    Rows = assessment.Rows.Where(row => row.Status == status).ToArray()
                })
                .Where(group => group.Rows.Length > 0)
                .ToArray();
            if (summaries.Count != expected.Length)
            {
                throw new DeterministicValidationException(
                    "Complete package assessments require one canonical factual summary group for every represented status.");
            }

            for (var index = 0; index < expected.Length; index++)
            {
                var summary = summaries[index];
                var group = expected[index];
                if (summary.Name != group.Status ||
                    !summary.RequirementIds.SequenceEqual(
                        group.Rows.Select(row => row.Id),
                        StringComparer.Ordinal))
                {
                    throw new DeterministicValidationException(
                        "Package summary groups must partition all selected repository-wide IDs exactly once by actual row status in canonical status order.");
                }
            }

            if (summaries.SelectMany(summary => summary.RequirementIds)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != assessment.Rows.Count)
            {
                throw new DeterministicValidationException(
                    "Package summary groups overlap or omit repository-wide requirement IDs.");
            }
        }

        foreach (var summary in summaries)
        {
            ValidateOptionalField(summary.Name, "summary group name");
            RequireSubstantive(summary.FactualSummary, "summary factual text");
            ValidateReferences(summary.RequirementIds, selectedIds, "summary requirement");
            ValidateEvidenceReferences(summary.EvidenceIds, selectedEvidence, "summary evidence");
            ValidateExactEvidenceUnion(
                summary.RequirementIds,
                summary.EvidenceIds,
                assessment.Rows,
                "summary");
            usedEvidence.UnionWith(summary.EvidenceIds);
        }
    }

    private static void ValidateExactEvidenceUnion(
        IReadOnlyList<string> requirementIds,
        IReadOnlyList<string> evidenceIds,
        IReadOnlyList<AssessmentRow> rows,
        string resource)
    {
        var selected = requirementIds.ToHashSet(StringComparer.Ordinal);
        var expected = rows
            .Where(row => selected.Contains(row.Id))
            .SelectMany(row => row.EvidenceIds)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!evidenceIds.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                $"{resource} evidence must equal the exact canonical union of its underlying row evidence.");
        }
    }

    private static void ValidateReferences(
        IReadOnlyList<string> references,
        IReadOnlyList<string> selected,
        string name)
    {
        if (references.Count == 0 ||
            references.Distinct(StringComparer.Ordinal).Count() != references.Count ||
            references.Any(reference => !selected.Contains(reference, StringComparer.Ordinal)) ||
            !references.SequenceEqual(
                selected.Where(id => references.Contains(id, StringComparer.Ordinal)),
                StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                $"{name} IDs must be nonempty, unique, selected, and in canonical rubric order.");
        }
    }

    private static void ValidateEvidenceReferences(
        IReadOnlyList<string> references,
        IReadOnlySet<string> selected,
        string name)
    {
        if (references.Distinct(StringComparer.Ordinal).Count() != references.Count ||
            references.Any(reference => !selected.Contains(reference)))
        {
            throw new DeterministicValidationException($"{name} IDs must be unique and selected.");
        }
    }

    private static void ValidateCompletion(ReadinessAssessment assessment)
    {
        if (!CompletionStates.Contains(assessment.CompletionState, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException("Unknown completion_state.");
        }

        var completed = assessment.Rows.Count(row => row.Status is not null);
        if (assessment.CompletionState == "complete" && completed != assessment.Rows.Count)
        {
            throw new DeterministicValidationException(
                "A complete assessment cannot contain incomplete placeholder rows.");
        }

        if (assessment.CompletionState == "targeted" &&
            (completed == 0 || completed == assessment.Rows.Count))
        {
            throw new DeterministicValidationException(
                "A targeted assessment must contain both assessed and explicitly incomplete rows.");
        }

        if (assessment.CompletionState == "incomplete" && completed == assessment.Rows.Count)
        {
            throw new DeterministicValidationException(
                "An assessment with every row completed cannot claim incomplete state.");
        }
    }

    private static void ValidateOptionalField(string? value, string name)
    {
        if (value is null)
        {
            return;
        }

        _ = ContractJson.NormalizeText(value, name, 4096);
        if (ContractJson.IsPlaceholder(value))
        {
            throw new DeterministicValidationException($"{name} cannot be blank, TODO, or TBD.");
        }
    }

    private static void RequireSubstantive(string? value, string name)
    {
        ValidateOptionalField(value, name);
        if (value is null || value.Length < 12)
        {
            throw new DeterministicValidationException($"{name} must be substantive and bounded.");
        }
    }

    private static void WriteIdentity(Utf8JsonWriter writer, ExactAssessmentIdentity identity)
    {
        writer.WriteStartObject();
        writer.WriteString("assessment_kind", identity.AssessmentKind);
        writer.WritePropertyName("package");
        WritePackage(writer, identity.Package);
        ContractJson.WriteDigest(writer, "input_manifest_sha256", identity.InputManifestDigest);
        WriteNullable(writer, "component_id", identity.ComponentId);
        writer.WriteEndObject();
    }

    private static void WritePackage(Utf8JsonWriter writer, EvidencePackageIdentity package)
    {
        writer.WriteStartObject();
        writer.WriteString("package_id", package.PackageId);
        writer.WriteString("version", package.Version);
        ContractJson.WriteDigest(writer, "nupkg_sha256", package.NupkgDigest);
        writer.WriteEndObject();
    }

    private static void WriteOverlay(Utf8JsonWriter writer, AssessmentOverlay overlay)
    {
        writer.WriteStartObject();
        writer.WriteString("id", overlay.Id);
        writer.WriteString("version", overlay.Version);
        ContractJson.WriteDigest(writer, "sha256", overlay.Digest);
        writer.WriteEndObject();
    }

    private static void WritePackageReference(Utf8JsonWriter writer, PackageAssessmentReference? reference)
    {
        if (reference is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName("package");
        WritePackage(writer, reference.Package);
        ContractJson.WriteDigest(writer, "input_manifest_sha256", reference.InputManifestDigest);
        WriteNullableDigest(writer, "assessment_sha256", reference.AssessmentDigest);
        WriteNullableDigest(writer, "report_sha256", reference.ReportDigest);
        ContractJson.WriteDigest(writer, "validation_sha256", reference.ValidationDigest);
        writer.WriteEndObject();
    }

    private static void WriteRow(Utf8JsonWriter writer, AssessmentRow row)
    {
        writer.WriteStartObject();
        writer.WriteString("id", row.Id);
        writer.WriteString("requirement", row.Requirement);
        writer.WriteString("scope", row.Scope);
        writer.WriteString("area", row.Area);
        WriteNullable(writer, "status", row.Status);
        WriteNullable(writer, "observation", row.Observation);
        writer.WritePropertyName("evidence_ids");
        WriteStrings(writer, row.EvidenceIds);
        WriteNullable(writer, "owner_action", row.OwnerAction);
        WriteNullable(writer, "assessment_follow_up", row.AssessmentFollowUp);
        WriteNullable(writer, "not_applicable_rationale", row.NotApplicableRationale);
        writer.WriteEndObject();
    }

    private static ExactAssessmentIdentity ParseIdentity(JsonElement element, bool requireCanonical)
    {
        RequireAssessmentProperties(
            element,
            requireCanonical,
            "assessment_kind",
            "package",
            "input_manifest_sha256",
            "component_id");
        return new ExactAssessmentIdentity(
            ContractJson.String(element, "assessment_kind"),
            ParsePackage(ContractJson.Object(element, "package"), requireCanonical),
            ParseAssessmentDigest(ContractJson.Object(element, "input_manifest_sha256"), requireCanonical),
            ContractJson.NullableString(element, "component_id"));
    }

    private static EvidencePackageIdentity ParsePackage(JsonElement element, bool requireCanonical)
    {
        RequireAssessmentProperties(element, requireCanonical, "package_id", "version", "nupkg_sha256");
        return new EvidencePackageIdentity(
            ContractJson.String(element, "package_id"),
            ContractJson.String(element, "version"),
            ParseAssessmentDigest(ContractJson.Object(element, "nupkg_sha256"), requireCanonical));
    }

    private static AssessmentOverlay ParseOverlay(JsonElement element, bool requireCanonical)
    {
        RequireAssessmentProperties(element, requireCanonical, "id", "version", "sha256");
        return new AssessmentOverlay(
            ContractJson.String(element, "id"),
            ContractJson.String(element, "version"),
            ParseAssessmentDigest(ContractJson.Object(element, "sha256"), requireCanonical));
    }

    private static PackageAssessmentReference? ParsePackageReference(JsonElement? element, bool requireCanonical)
    {
        if (element is null)
        {
            return null;
        }

        var value = element.Value;
        RequireAssessmentProperties(
            value,
            requireCanonical,
            "package",
            "input_manifest_sha256",
            "assessment_sha256",
            "report_sha256",
            "validation_sha256");
        return new PackageAssessmentReference(
            ParsePackage(ContractJson.Object(value, "package"), requireCanonical),
            ParseAssessmentDigest(ContractJson.Object(value, "input_manifest_sha256"), requireCanonical),
            ParseNullableDigest(value, "assessment_sha256", requireCanonical),
            ParseNullableDigest(value, "report_sha256", requireCanonical),
            ParseAssessmentDigest(ContractJson.Object(value, "validation_sha256"), requireCanonical));
    }

    private static AssessmentRow ParseRow(JsonElement element, bool requireCanonical)
    {
        RequireAssessmentProperties(
            element,
            requireCanonical,
            "id",
            "requirement",
            "scope",
            "area",
            "status",
            "observation",
            "evidence_ids",
            "owner_action",
            "assessment_follow_up",
            "not_applicable_rationale");
        return new AssessmentRow(
            ContractJson.String(element, "id"),
            ContractJson.String(element, "requirement"),
            ContractJson.String(element, "scope"),
            ContractJson.String(element, "area"),
            ContractJson.NullableString(element, "status"),
            ContractJson.NullableString(element, "observation"),
            ContractJson.StringArray(element, "evidence_ids"),
            ContractJson.NullableString(element, "owner_action"),
            ContractJson.NullableString(element, "assessment_follow_up"),
            ContractJson.NullableString(element, "not_applicable_rationale"));
    }

    private static AssessmentFinding ParseFinding(JsonElement element, bool requireCanonical)
    {
        RequireAssessmentProperties(element, requireCanonical, "title", "factual_summary", "requirement_ids", "evidence_ids");
        return new AssessmentFinding(
            ContractJson.String(element, "title"),
            ContractJson.String(element, "factual_summary"),
            ContractJson.StringArray(element, "requirement_ids"),
            ContractJson.StringArray(element, "evidence_ids"));
    }

    private static AssessmentSummaryGroup ParseSummary(JsonElement element, bool requireCanonical)
    {
        RequireAssessmentProperties(element, requireCanonical, "name", "factual_summary", "requirement_ids", "evidence_ids");
        return new AssessmentSummaryGroup(
            ContractJson.String(element, "name"),
            ContractJson.String(element, "factual_summary"),
            ContractJson.StringArray(element, "requirement_ids"),
            ContractJson.StringArray(element, "evidence_ids"));
    }

    private static Sha256Digest? ParseNullableDigest(JsonElement element, string property, bool requireCanonical)
    {
        var value = ContractJson.NullableObject(element, property);
        return value is null ? null : ParseAssessmentDigest(value.Value, requireCanonical);
    }

    private static Sha256Digest ParseAssessmentDigest(JsonElement element, bool requireCanonical)
    {
        RequireAssessmentProperties(element, requireCanonical, "algorithm", "value");
        var digest = new Sha256Digest(ContractJson.String(element, "algorithm"), ContractJson.String(element, "value"));
        EvidenceIdentity.ValidateDigest(digest, "digest");
        return digest;
    }

    private static void RequireAssessmentProperties(JsonElement element, bool requireCanonical, params string[] names)
    {
        if (requireCanonical)
        {
            ContractJson.RequireProperties(element, names);
        }
        else
        {
            ContractJson.RequirePropertiesUnordered(element, names);
        }
    }

    private static void WriteStrings(Utf8JsonWriter writer, IEnumerable<string> values)
    {
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WriteNullable(Utf8JsonWriter writer, string property, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(property);
        }
        else
        {
            writer.WriteString(property, value);
        }
    }

    private static void WriteNullableDigest(Utf8JsonWriter writer, string property, Sha256Digest? digest)
    {
        if (digest is null)
        {
            writer.WriteNull(property);
        }
        else
        {
            ContractJson.WriteDigest(writer, property, digest);
        }
    }
}
