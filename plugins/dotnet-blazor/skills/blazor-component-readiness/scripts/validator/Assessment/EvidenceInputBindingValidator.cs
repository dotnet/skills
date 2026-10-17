using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Evidence;
using BlazorComponentReadiness.Validator.IO;

namespace BlazorComponentReadiness.Validator.Assessment;

public static class EvidenceInputBindingValidator
{
    public static void Validate(
        string root,
        ReadinessAssessment assessment,
        InputManifest input,
        EvidenceBundle evidence) =>
        Validate(root, assessment.Identity, input, evidence);

    public static void Validate(
        string root,
        ExactAssessmentIdentity identity,
        InputManifest input,
        EvidenceBundle evidence)
    {
        var selected = evidence.Selection
            .Select(selection => selection.EvidenceId)
            .ToHashSet(StringComparer.Ordinal);
        var records = evidence.SourceLedgers
            .SelectMany(source => source.Ledger.Records)
            .Where(record => selected.Contains(record.StableId))
            .ToDictionary(record => record.StableId, StringComparer.Ordinal);
        var packagePath = SafePath.ResolveUnderRoot(
            root,
            input.Package.NupkgPath,
            requireExisting: true,
            requireFile: true);
        var component = identity.ComponentId is null
            ? null
            : input.Components.Single(item => item.Id == identity.ComponentId);

        foreach (var selection in evidence.Selection)
        {
            var record = records[selection.EvidenceId];
            var provenance = record.Provenance;
            var valid = provenance.Kind switch
            {
                EvidenceIdentity.VendorPublicDocumentation =>
                    input.Documentation.Any(document =>
                        document.Url == provenance.Locator &&
                        document.ContentDigest == provenance.ContentDigest),
                EvidenceIdentity.PackageArtifactMetadata =>
                    ValidatePackageArtifact(packagePath, input, provenance),
                EvidenceIdentity.OwnerSuppliedInternalEvidence or
                    EvidenceIdentity.OwnerSuppliedPublicEvidence =>
                    input.OwnerInputs.Any(owner =>
                        owner.Basename == provenance.Locator &&
                        owner.Provenance == provenance.Kind &&
                        owner.ContentDigest == provenance.ContentDigest),
                EvidenceIdentity.VendorSourceRepository =>
                    ValidateSourceArtifact(component, input, provenance),
                EvidenceIdentity.OwnerDeclaredClosedSource =>
                    input.Source.Availability == "closed-source",
                EvidenceIdentity.ReproducedRuntimeObservation =>
                    IsStructuredProtocol(provenance.Method)
                        ? ValidateEvidenceInput(input, provenance)
                        : true,
                EvidenceIdentity.ReviewerGeneratedAnalysis =>
                    ValidateEvidenceInput(input, provenance),
                EvidenceIdentity.OwnerDeclaredUnavailable => true,
                _ => false
            };
            if (!valid)
            {
                var detail = provenance.Kind == EvidenceIdentity.ReviewerGeneratedAnalysis ||
                             provenance.Kind == EvidenceIdentity.ReproducedRuntimeObservation &&
                             IsStructuredProtocol(provenance.Method)
                    ? input.EvidenceInputs.Any(item => item.Basename == provenance.Locator)
                        ? "The registered evidence basename has a different content digest."
                        : "The locator must be an exact registered evidence_inputs basename, not a producer label or command."
                    : "The provenance locator, digest or declared source does not match its confirmed input binding rule.";
                throw new DeterministicValidationException(
                    $"Selected evidence '{record.StableId}' (kind '{provenance.Kind}', locator '{provenance.Locator}') " +
                    $"is not bound to the confirmed input manifest sha256:{identity.InputManifestDigest.Value}. {detail} " +
                    "Register the retained artifact through candidates, discover and explicitly confirm a new manifest, " +
                    "then rebuild the final identity, evidence ledger and input-bound bundle. Do not mutate earlier manifests.");
            }
        }
    }

    private static bool ValidatePackageArtifact(
        string packagePath,
        InputManifest input,
        EvidenceProvenance provenance)
    {
        var computed = NupkgInspector.ComputeEvidenceContentSha256(
            packagePath,
            provenance.Locator);
        if (computed != provenance.ContentDigest.Value)
        {
            return false;
        }

        if (provenance.Locator == NupkgInspector.WholePackageEvidenceLocator)
        {
            return provenance.ContentDigest == input.Package.NupkgDigest;
        }

        var captured = input.PackageSources
            .Where(source => source.Locator == provenance.Locator)
            .ToArray();
        return captured.Length == 0 ||
            captured.Length == 1 && captured[0].ContentDigest == provenance.ContentDigest;
    }

    private static bool ValidateSourceArtifact(
        InputComponent? component,
        InputManifest input,
        EvidenceProvenance provenance)
    {
        const string Prefix = "source:";
        if (!provenance.Locator.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var sourcePath = provenance.Locator[Prefix.Length..];
        if (component is not null &&
            !component.AllowedSourcePaths.Contains(sourcePath, StringComparer.Ordinal))
        {
            return false;
        }

        return input.Source.Availability == "source-available" &&
            input.SourceArtifacts.Any(artifact =>
                artifact.SourcePath == sourcePath &&
                artifact.ContentDigest == provenance.ContentDigest);
    }

    private static bool ValidateEvidenceInput(
        InputManifest input,
        EvidenceProvenance provenance) =>
        input.EvidenceInputs.Any(evidenceInput =>
            evidenceInput.Basename == provenance.Locator &&
            evidenceInput.ContentDigest == provenance.ContentDigest);

    private static bool IsStructuredProtocol(string method) =>
        method.StartsWith("protocol:", StringComparison.Ordinal);
}
