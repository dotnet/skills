using System.Text;
using System.Text.Json;
using BlazorComponentReadiness.Validator.Assessment;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Validation;

namespace BlazorComponentReadiness.Validator.Rendering;

public static class ReportService
{
    public static readonly IReadOnlyList<string> Limitations =
    [
        "This report is a structural self-assessment against a versioned public baseline; it is not certification or a Microsoft acceptance requirement.",
        "Deterministic validation proves local artifact correspondence and schema rules, not factual truth, organizational approval, or release suitability."
    ];

    /// <summary>
    /// Renders canonical assessment facts. An authorized scope requires its retained input root.
    /// </summary>
    public static byte[] RenderMarkdown(
        ReadinessAssessment assessment,
        InputManifest input,
        EvidenceBundle evidence,
        AssessmentFeedback? feedback = null,
        string? root = null,
        ScopedPackageContextBinding? scopedPackageContext = null)
    {
        var profile = ScopedComponentProfile.Load(root, input);
        if (profile is not null)
        {
            InputManifestService.Validate(input, root!, requireConfirmed: true);
            AssessmentService.Validate(root!, assessment, AssessmentService.Serialize(assessment),
                input, InputManifestService.Serialize(input), evidence, scopedPackageContext: scopedPackageContext);
            profile.RejectDisclosure(AssessmentService.Serialize(assessment), json: true);
        }
        else if (scopedPackageContext is not null)
            throw new DeterministicValidationException("Ordinary reports cannot use scoped package context.");
        else
            ScopedComponentProfile.RejectUnboundComponent(assessment);
        var authorizedScope = AuthorizedPackageScope.Load(root, input);
        authorizedScope?.Validate(assessment, input, evidence);
        var provenance = evidence.SourceLedgers
            .SelectMany(ledger => ledger.Ledger.Records)
            .ToDictionary(record => record.StableId, record => record.Provenance.Kind, StringComparer.Ordinal);
        var lines = new List<string>
        {
            $"# Blazor component readiness: {assessment.AssessmentKind}",
            "",
            $"**Completion state:** `{assessment.CompletionState}`",
            $"**Package:** `{input.Package.PackageId}` `{input.Package.Version}`",
            assessment.Identity.ComponentId is null
                ? "**Component:** package-wide"
                : $"**Component:** `{assessment.Identity.ComponentId}`",
            $"**Rubric:** `{assessment.RubricVersion}`",
            "",
            "## Assessment inputs",
            "",
            $" - Acquisition: `{input.Acquisition}`",
            $" - Package origin: {Escape(input.Package.OriginLocator)}",
            $" - Package retrieval method: `{input.Package.RetrievalMethod}`",
            $" - Exact nupkg SHA-256: `{input.Package.NupkgDigest.Value}`",
            $" - Source availability: `{input.Source.Availability}`"
        };
        if (authorizedScope is not null)
        {
            lines.InsertRange(7, [authorizedScope.Declaration, ""]);
        }
        if (profile is not null)
            lines.InsertRange(7, [profile.Declaration, "", ScopedComponentProfile.ExportNotice, ""]);

        if (input.Source.RepositoryUri is not null)
        {
            lines.Add($" - Repository: {input.Source.RepositoryUri}");
        }

        if (input.Source.Commit is not null)
        {
            lines.Add($" - Exact source commit: `{input.Source.Commit}`");
            lines.Add($" - Source-to-package mapping: {Escape(input.Source.Mapping!)} (`{input.Source.Confidence}` confidence)");
        }

        lines.Add(" - Retrieval attempts:");
        lines.AddRange(input.RetrievalAttempts.Select(attempt =>
            $"   - `{attempt.Subject}` via `{attempt.RetrievalMethod}` from {Escape(attempt.Locator)}: " +
            $"`{attempt.Result}`{(attempt.Detail is null ? "" : $" ({Escape(attempt.Detail)})")}"));
        lines.Add(" - Official vendor documentation:");
        lines.AddRange(input.Documentation.Count == 0
            ? ["   - None recorded."]
            : input.Documentation.Select(document =>
                $"   - {document.Url} (`{document.ContentDigest.Value}`)"));
        lines.Add(" - Package README/metadata sources:");
        lines.AddRange(input.PackageSources.Count == 0
            ? ["   - None recorded."]
            : input.PackageSources.Select(source =>
                $"   - `{source.Kind}` {Escape(source.Locator)} (`{source.ContentDigest.Value}`)"));
        lines.Add(" - Confirmed source artifacts:");
        lines.AddRange(input.SourceArtifacts.Count == 0
            ? ["   - None recorded."]
            : input.SourceArtifacts.Select(artifact =>
                $"   - `source:{Escape(artifact.SourcePath)}` (`{artifact.ContentDigest.Value}`)"));
        lines.Add(" - Owner-supplied inputs:");
        lines.AddRange(input.OwnerInputs.Count == 0
            ? ["   - None recorded."]
            : input.OwnerInputs.Select(owner =>
                $"   - `{Escape(owner.Basename)}` [{owner.Provenance}] (`{owner.ContentDigest.Value}`)"));
        lines.Add(" - Confirmed component inventory:");
        lines.AddRange(input.Components.Count == 0
            ? ["   - no components selected for this package assessment"]
            : input.Components.Select(component =>
                $"   - `{component.Id}` ({string.Join(", ", component.RenderModes.Select(mode => $"`{mode}`"))})"));
        lines.Add(" - Explicit exclusions:");
        lines.AddRange(input.Exclusions.Count == 0
            ? ["   - None recorded."]
            : input.Exclusions.Select(exclusion =>
                $"   - {Escape(exclusion.Subject)}: {Escape(exclusion.Rationale)}"));

        lines.AddRange(["", "## Factual status counts", ""]);
        foreach (var status in RubricLoader.Load(assessment.RubricVersion).Statuses)
        {
            lines.Add($"- `{status}`: {assessment.Rows.Count(row => row.Status == status)}");
        }

        lines.Add($"- `incomplete`: {assessment.Rows.Count(row => row.Status is null)}");
        lines.AddRange(["", "## Factual summaries", ""]);
        if (assessment.SummaryGroups.Count == 0 && assessment.Findings.Count == 0)
        {
            lines.Add("No factual summary groups or findings were supplied.");
        }
        else
        {
            foreach (var summary in assessment.SummaryGroups)
            {
                lines.Add($"### {Escape(summary.Name)}");
                lines.Add("");
                lines.Add(Escape(summary.FactualSummary));
                lines.Add("");
                lines.Add($"Requirements: {string.Join(", ", summary.RequirementIds.Select(id => $"`{id}`"))}");
                lines.Add($"Evidence: {RenderEvidence(summary.EvidenceIds, provenance)}");
                lines.Add("");
            }

            foreach (var finding in assessment.Findings)
            {
                lines.Add($"### {Escape(finding.Title)}");
                lines.Add("");
                lines.Add(Escape(finding.FactualSummary));
                lines.Add("");
                lines.Add($"Requirements: {string.Join(", ", finding.RequirementIds.Select(id => $"`{id}`"))}");
                lines.Add($"Evidence: {RenderEvidence(finding.EvidenceIds, provenance)}");
                lines.Add("");
            }
        }

        if (feedback is not null)
        {
            lines.AddRange(
            [
                "",
                "## Assessment feedback",
                "",
                "| Requirement IDs | Feedback |",
                "|---|---|"
            ]);
            foreach (var entry in feedback.Entries)
            {
                lines.Add(
                    $"| {string.Join(", ", entry.RequirementIds.Select(id => $"`{id}`"))} |" +
                    $"{entry.RawPayload}|");
            }
        }

        lines.AddRange(
        [
            "",
            "## Canonical requirements",
            "",
            "| ID | Scope | Area | Requirement | Status | Factual observation | Evidence and provenance | Owner action | Assessment follow-up / rationale |",
            "|---|---|---|---|---|---|---|---|---|"
        ]);
        foreach (var row in assessment.Rows)
        {
            var final = row.AssessmentFollowUp ?? row.NotApplicableRationale;
            lines.Add(
                $"| `{row.Id}` | `{row.Scope}` | {Escape(row.Area)} | {Escape(row.Requirement)} | " +
                $"{(row.Status is null ? "_incomplete_" : $"`{row.Status}`")} | " +
                $"{Escape(row.Observation)} | {RenderEvidence(row.EvidenceIds, provenance)} | " +
                $"{Escape(row.OwnerAction)} | {Escape(final)} |");
        }

        lines.AddRange(["", "## Limitations", ""]);
        lines.AddRange(Limitations.Select(limitation => $"- {limitation}"));
        lines.Add("");
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        BoundedIO.EnsureLength(bytes.Length, ResourceLimits.SerializedArtifactBytes, "rendered report");
        authorizedScope?.RejectDisclosure(Encoding.UTF8.GetString(bytes));
        profile?.RejectDisclosure(bytes);
        return bytes;
    }

    /// <summary>
    /// Binds the exact artifacts, including any authorized scope through the confirmed input digest.
    /// </summary>
    public static ValidationManifest CreateManifest(
        ReadinessAssessment assessment,
        ReadOnlySpan<byte> assessmentBytes,
        InputManifest input,
        ReadOnlySpan<byte> inputBytes,
        EvidenceBundle evidence,
        ReadOnlySpan<byte> evidenceBytes,
        ReadOnlySpan<byte> reportBytes,
        Sha256Digest? predecessorManifestDigest = null,
        Sha256Digest? feedbackDigest = null,
        IReadOnlyList<string>? declaredChangedIds = null,
        string? root = null,
        ScopedPackageContextBinding? scopedPackageContext = null)
    {
        var profile = ScopedComponentProfile.Load(root, input);
        if (profile is not null)
        {
            InputManifestService.Validate(input, root!, requireConfirmed: true);
            AssessmentService.Validate(root!, assessment, assessmentBytes, input, inputBytes, evidence,
                scopedPackageContext: scopedPackageContext);
            ContractJson.RequireCanonical(inputBytes, InputManifestService.Serialize(input), "profile-bound input");
            ContractJson.RequireCanonical(assessmentBytes, AssessmentService.Serialize(assessment), "profile-bound assessment");
            ContractJson.RequireCanonical(evidenceBytes, CanonicalEvidenceJson.SerializeBundle(evidence), "profile-bound evidence");
            profile.RejectDisclosure(assessmentBytes.ToArray(), json: true);
            profile.RejectDisclosure(reportBytes.ToArray());
            if (!Encoding.UTF8.GetString(reportBytes).Contains(profile.Declaration, StringComparison.Ordinal) ||
                feedbackDigest is null && !reportBytes.SequenceEqual(
                    RenderMarkdown(assessment, input, evidence, root: root, scopedPackageContext: scopedPackageContext)))
                throw new DeterministicValidationException("Profile-bound receipt requires the exact scoped component report.");
        }
        else if (scopedPackageContext is not null)
            throw new DeterministicValidationException("Ordinary validation manifests cannot use scoped package context.");
        else
            ScopedComponentProfile.RejectUnboundComponent(assessment);
        var authorizedScope = AuthorizedPackageScope.Load(root, input);
        if (authorizedScope is not null)
        {
            authorizedScope.Validate(assessment, input, evidence);
            ContractJson.RequireCanonical(inputBytes, InputManifestService.Serialize(input), "scope-bound input");
            ContractJson.RequireCanonical(assessmentBytes, AssessmentService.Serialize(assessment), "scope-bound assessment");
            ContractJson.RequireCanonical(evidenceBytes, CanonicalEvidenceJson.SerializeBundle(evidence), "scope-bound evidence");
            var report = Encoding.UTF8.GetString(reportBytes);
            authorizedScope.RejectDisclosure(report);
            if (!report.Contains(authorizedScope.Declaration, StringComparison.Ordinal))
            {
                throw new DeterministicValidationException("Report does not declare its exact authorized scope.");
            }

            if (feedbackDigest is null &&
                !reportBytes.SequenceEqual(RenderMarkdown(assessment, input, evidence, root: root)))
            {
                throw new DeterministicValidationException("Scope-bound receipt requires the exact rendered report.");
            }
        }

        return new ValidationManifest(
            1,
            ContractVersions.PluginVersion,
            ContractVersions.ValidatorVersion,
            RendererContract.Version,
            assessment.RubricVersion,
            assessment.ScopeSchemaVersion,
            assessment.RubricDigest,
            assessment.ScopeMapDigest,
            assessment.Overlays,
            InputManifestService.Digest(inputBytes),
            ContractJson.RawDigest(assessmentBytes),
            ContractJson.RawDigest(evidenceBytes),
            evidence.Selection.OrderBy(item => item.DisplayOrder).Select(item => item.EvidenceId).ToArray(),
            assessment.PackageReference,
            ContractJson.RawDigest(reportBytes),
            assessment.AssessmentKind,
            assessment.CompletionState,
            predecessorManifestDigest,
            feedbackDigest,
            (declaredChangedIds ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Limitations);
    }

    public static byte[] SerializeManifest(ValidationManifest manifest)
    {
        ValidateDeclaredChangedIds(manifest);
        var bytes = StrictJson.SerializeCanonical(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", manifest.SchemaVersion);
            writer.WriteString("plugin_version", manifest.PluginVersion);
            writer.WriteString("validator_version", manifest.ValidatorVersion);
            writer.WriteString("renderer_version", manifest.RendererVersion);
            writer.WriteString("rubric_version", manifest.RubricVersion);
            writer.WriteNumber("scope_schema_version", manifest.ScopeSchemaVersion);
            ContractJson.WriteDigest(writer, "rubric_sha256", manifest.RubricDigest);
            ContractJson.WriteDigest(writer, "scope_map_sha256", manifest.ScopeMapDigest);
            writer.WritePropertyName("overlays");
            writer.WriteStartArray();
            foreach (var overlay in manifest.Overlays)
            {
                writer.WriteStartObject();
                writer.WriteString("id", overlay.Id);
                writer.WriteString("version", overlay.Version);
                ContractJson.WriteDigest(writer, "sha256", overlay.Digest);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            ContractJson.WriteDigest(writer, "input_manifest_sha256", manifest.InputManifestDigest);
            ContractJson.WriteDigest(writer, "assessment_sha256", manifest.AssessmentDigest);
            ContractJson.WriteDigest(writer, "evidence_sha256", manifest.EvidenceDigest);
            writer.WritePropertyName("selected_evidence_ids");
            WriteStrings(writer, manifest.SelectedEvidenceIds);
            writer.WritePropertyName("package_reference");
            WritePackageReference(writer, manifest.PackageReference);
            ContractJson.WriteDigest(writer, "report_sha256", manifest.ReportDigest);
            writer.WriteString("assessment_kind", manifest.AssessmentKind);
            writer.WriteString("completion_state", manifest.CompletionState);
            WriteNullableDigest(writer, "predecessor_manifest_sha256", manifest.PredecessorManifestDigest);
            WriteNullableDigest(writer, "feedback_sha256", manifest.FeedbackDigest);
            writer.WritePropertyName("declared_changed_ids");
            WriteStrings(writer, manifest.DeclaredChangedIds);
            writer.WritePropertyName("limitations");
            WriteStrings(writer, manifest.Limitations);
            writer.WriteEndObject();
        });
        BoundedIO.EnsureLength(bytes.Length, ResourceLimits.SerializedArtifactBytes, "validation manifest");
        return bytes;
    }

    public static ValidationManifest ParseManifest(ReadOnlyMemory<byte> bytes)
    {
        using var document = StrictJson.Parse(bytes, ResourceLimits.SerializedArtifactBytes, "validation manifest");
        var root = document.RootElement;
        ContractJson.RequireProperties(
            root,
            "schema_version",
            "plugin_version",
            "validator_version",
            "renderer_version",
            "rubric_version",
            "scope_schema_version",
            "rubric_sha256",
            "scope_map_sha256",
            "overlays",
            "input_manifest_sha256",
            "assessment_sha256",
            "evidence_sha256",
            "selected_evidence_ids",
            "package_reference",
            "report_sha256",
            "assessment_kind",
            "completion_state",
            "predecessor_manifest_sha256",
            "feedback_sha256",
            "declared_changed_ids",
            "limitations");
        var predecessor = ContractJson.NullableObject(root, "predecessor_manifest_sha256");
        var feedback = ContractJson.NullableObject(root, "feedback_sha256");
        var manifest = new ValidationManifest(
            ContractJson.Int32(root, "schema_version"),
            ContractJson.String(root, "plugin_version"),
            ContractJson.String(root, "validator_version"),
            ContractJson.String(root, "renderer_version"),
            ContractJson.String(root, "rubric_version"),
            ContractJson.Int32(root, "scope_schema_version"),
            ContractJson.Digest(ContractJson.Object(root, "rubric_sha256")),
            ContractJson.Digest(ContractJson.Object(root, "scope_map_sha256")),
            ContractJson.Array(root, "overlays").EnumerateArray().Select(ParseOverlay).ToArray(),
            ContractJson.Digest(ContractJson.Object(root, "input_manifest_sha256")),
            ContractJson.Digest(ContractJson.Object(root, "assessment_sha256")),
            ContractJson.Digest(ContractJson.Object(root, "evidence_sha256")),
            ContractJson.StringArray(root, "selected_evidence_ids"),
            ParsePackageReference(ContractJson.NullableObject(root, "package_reference")),
            ContractJson.Digest(ContractJson.Object(root, "report_sha256")),
            ContractJson.String(root, "assessment_kind"),
            ContractJson.String(root, "completion_state"),
            predecessor is null ? null : ContractJson.Digest(predecessor.Value),
            feedback is null ? null : ContractJson.Digest(feedback.Value),
            ContractJson.StringArray(root, "declared_changed_ids"),
            ContractJson.StringArray(root, "limitations"));
        ContractJson.RequireCanonical(bytes.Span, SerializeManifest(manifest), "validation manifest");
        return manifest;
    }

    public static void ValidateManifest(ValidationManifest actual, ValidationManifest expected)
    {
        if (!SerializeManifest(actual).AsSpan().SequenceEqual(SerializeManifest(expected)) ||
            actual.SchemaVersion != 1 ||
            actual.PredecessorManifestDigest is null && actual.DeclaredChangedIds.Count != 0 ||
            !actual.Limitations.SequenceEqual(Limitations, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                "Validation manifest does not bind the exact renderer, rubric, input, assessment, evidence, package reference, report, kind, completion, and limitations.");
        }
    }

    private static string RenderEvidence(
        IReadOnlyList<string> evidenceIds,
        IReadOnlyDictionary<string, string> provenance) =>
        evidenceIds.Count == 0
            ? ""
            : string.Join("<br>", evidenceIds.Select(id => $"`{id}` [{provenance[id]}]"));

    private static string Escape(string? value) =>
        value is null ? "" : value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);

    private static void WriteStrings(Utf8JsonWriter writer, IEnumerable<string> values)
    {
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
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
        writer.WriteStartObject();
        writer.WriteString("package_id", reference.Package.PackageId);
        writer.WriteString("version", reference.Package.Version);
        ContractJson.WriteDigest(writer, "nupkg_sha256", reference.Package.NupkgDigest);
        writer.WriteEndObject();
        ContractJson.WriteDigest(writer, "input_manifest_sha256", reference.InputManifestDigest);
        WriteNullableDigest(writer, "assessment_sha256", reference.AssessmentDigest);
        WriteNullableDigest(writer, "report_sha256", reference.ReportDigest);
        ContractJson.WriteDigest(writer, "validation_sha256", reference.ValidationDigest);
        writer.WriteEndObject();
    }

    private static PackageAssessmentReference? ParsePackageReference(JsonElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var value = element.Value;
        ContractJson.RequireProperties(
            value,
            "package",
            "input_manifest_sha256",
            "assessment_sha256",
            "report_sha256",
            "validation_sha256");
        var package = ContractJson.Object(value, "package");
        ContractJson.RequireProperties(package, "package_id", "version", "nupkg_sha256");
        return new PackageAssessmentReference(
            new EvidencePackageIdentity(
                ContractJson.String(package, "package_id"),
                ContractJson.String(package, "version"),
                ContractJson.Digest(ContractJson.Object(package, "nupkg_sha256"))),
            ContractJson.Digest(ContractJson.Object(value, "input_manifest_sha256")),
            ParseNullableDigest(value, "assessment_sha256"),
            ParseNullableDigest(value, "report_sha256"),
            ContractJson.Digest(ContractJson.Object(value, "validation_sha256")));
    }

    private static AssessmentOverlay ParseOverlay(JsonElement element)
    {
        ContractJson.RequireProperties(element, "id", "version", "sha256");
        return new AssessmentOverlay(
            ContractJson.String(element, "id"),
            ContractJson.String(element, "version"),
            ContractJson.Digest(ContractJson.Object(element, "sha256")));
    }

    private static Sha256Digest? ParseNullableDigest(JsonElement element, string property)
    {
        var value = ContractJson.NullableObject(element, property);
        return value is null ? null : ContractJson.Digest(value.Value);
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

    private static void ValidateDeclaredChangedIds(ValidationManifest manifest)
    {
        var canonical = manifest.DeclaredChangedIds
            .Select(id => ContractJson.NormalizeText(id, "declared changed ID", 64))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!canonical.SequenceEqual(manifest.DeclaredChangedIds, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                "declared_changed_ids must be unique and canonically sorted.");
        }
    }
}
