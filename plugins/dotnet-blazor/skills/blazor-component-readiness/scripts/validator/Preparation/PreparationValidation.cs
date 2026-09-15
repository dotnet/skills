using System.Globalization;
using BlazorComponentReadiness.Validator.Cli;
using BlazorComponentReadiness.Validator.Contracts;
using BlazorComponentReadiness.Validator.Inputs;
using BlazorComponentReadiness.Validator.IO;
using BlazorComponentReadiness.Validator.Release;

namespace BlazorComponentReadiness.Validator.Preparation;

public static partial class PreparationService
{
    public static PreparationReceipt Validate(string root, string receiptPath, InputManifest finalInput)
    {
        root = Path.GetFullPath(root);
        receiptPath = Relative(root, receiptPath);
        var receipt = PreparationJson.Parse<PreparationReceipt>(BoundedIO.ReadAllBytes(
            FullPath(root, receiptPath, true), ResourceLimits.SerializedArtifactBytes, "preparation receipt"));
        Require(receiptPath == receipt.Request.Output + "/preparation.receipt.json", "Receipt is not at its sealed generation path.");
        var (input, map) = ValidateContents(root, receipt);
        InputManifestService.Validate(finalInput, root, requireConfirmed: true);
        var originals = input.EvidenceInputs.Select(item => item.Basename).ToHashSet(StringComparer.Ordinal);
        var additions = finalInput.EvidenceInputs.Where(item => !originals.Contains(item.Basename)).ToArray();
        foreach (var addition in additions)
        {
            var matches = map.Entries.Where(entry => entry.Basename == addition.Basename).ToArray();
            Require(matches.Length == 1 && matches[0].Kind == addition.Kind &&
                matches[0].Retained.Size == addition.Size && matches[0].Retained.Sha256 == addition.ContentDigest.Value,
                "Final input contains an unmapped or incorrectly typed preparation addition: " + addition.Basename);
        }
        var withoutAdditions = finalInput with
        {
            EvidenceInputs = finalInput.EvidenceInputs.Where(item => originals.Contains(item.Basename)).ToArray()
        };
        Require(InputManifestService.Serialize(input).AsSpan().SequenceEqual(InputManifestService.Serialize(withoutAdditions)),
            "Final preparation input must preserve every original field and registration; only mapped evidence additions are allowed.");
        return receipt;
    }

    private static (InputManifest Input, PreparationRegistrationMap Map) ValidateContents(string root, PreparationReceipt receipt)
    {
        Require(receipt.SchemaVersion == 1 && receipt.Profile == Profile && receipt.ProducerVersion == Version &&
            Guid.TryParseExact(receipt.GenerationId, "N", out _) &&
            receipt.GenerationId == receipt.GenerationId.ToLowerInvariant(), "Unsupported preparation receipt identity.");
        Require(Time(receipt.StartedAtUtc) <= Time(receipt.CompletedAtUtc), "Invalid preparation execution interval.");
        try { PreparationCommand.ValidateOptions(receipt.Request); }
        catch (UsageException exception) { throw new DeterministicValidationException(exception.Message, exception); }
        Require(Relative(root, receipt.Request.Input) == receipt.Request.Input &&
            Relative(root, receipt.Request.Output) == receipt.Request.Output, "Receipt request paths must be root-relative.");
        Require(receipt.PreInput.Path == receipt.Request.Input &&
            receipt.InputSnapshot.Path == receipt.Request.Output + "/input.snapshot.json" &&
            receipt.Roles.Path == receipt.Request.Output + "/roles.json" &&
            receipt.Reservation.Path == receipt.Request.Output + ".preparation-lock" &&
            receipt.RegistrationMap.Path == receipt.Request.Output + "/registration-map.json", "Invalid generation binding paths.");
        var (inputBytes, input, subject) = LoadInput(root, receipt.Request.Input);
        Require(PreparationJson.Same(subject, receipt.Subject), "Preparation subject differs from current confirmed input/scope.");
        Require(Read(root, receipt.PreInput).AsSpan().SequenceEqual(inputBytes) &&
            Read(root, receipt.InputSnapshot).AsSpan().SequenceEqual(inputBytes), "Pre-input snapshot changed.");
        Require(System.Text.Encoding.UTF8.GetString(Read(root, receipt.Reservation)) == receipt.GenerationId,
            "Preparation generation reservation mismatch.");
        var roles = PreparationJson.Parse<PreparationRole[]>(Read(root, receipt.Roles));
        Require(PreparationJson.Same(roles, BindRoles(root, input, receipt.Request)), "Resolved raw role bindings changed.");
        Require(receipt.Operations.Count == OperationIds.Length &&
            receipt.Operations.Select(op => op.Id).SequenceEqual(OperationIds), "Preparation requires each fixed operation exactly once in order.");
        FullSourceInventory? inventory = null;
        var references = new List<PreparationArtifact> { receipt.InputSnapshot, receipt.Roles, receipt.RegistrationMap };
        foreach (var operation in receipt.Operations)
        {
            var expectedPath = receipt.Request.Output + "/" + operation.Id;
            Require(operation.Invocation.Path == expectedPath + ".invocation.json" &&
                operation.Diagnostic.Path == expectedPath + ".diagnostic.json" &&
                (operation.Output is null || operation.Output.Path == expectedPath + ".json"), "Operation references escaped their generation.");
            Require(operation.Dependencies.SequenceEqual(Dependencies(operation.Id)), "Incorrect operation dependencies.");
            var invocation = PreparationJson.Parse<PreparationInvocation>(Read(root, operation.Invocation));
            var diagnostic = PreparationJson.Parse<PreparationDiagnostic>(Read(root, operation.Diagnostic));
            Require(invocation.GenerationId == receipt.GenerationId && invocation.OperationId == operation.Id &&
                invocation.CollectorVersion == Collector(operation.Id) && invocation.Root == root &&
                PreparationJson.Same(invocation.WrapperRequest, receipt.Request), "Operation invocation identity/parameters changed.");
            Require(diagnostic.GenerationId == receipt.GenerationId && diagnostic.OperationId == operation.Id &&
                diagnostic.Outcome == operation.Outcome && diagnostic.Cause == operation.Cause &&
                !string.IsNullOrWhiteSpace(diagnostic.Detail) && diagnostic.Detail.Length <= 8250,
                "Operation diagnostic identity/outcome/size mismatch.");
            Require(Time(invocation.StartedAtUtc) >= Time(receipt.StartedAtUtc) &&
                Time(diagnostic.CompletedAtUtc) >= Time(invocation.StartedAtUtc) &&
                Time(diagnostic.CompletedAtUtc) <= Time(receipt.CompletedAtUtc), "Invalid operation execution interval.");
            references.Add(operation.Invocation);
            references.Add(operation.Diagnostic);
            var missing = Missing(receipt.Request, operation.Id, inventory is not null);
            var expectedCall = missing is not null ? null : ResolveCall(operation.Id, root, receipt.Request, input, roles, inventory);
            Require(PreparationJson.Same(invocation.Call, expectedCall) &&
                invocation.OutputPath == (expectedCall is null || operation.Id == "input.validate" ? null : expectedPath + ".json"),
                "Resolved collector call or producer output differs from actual dispatch bindings.");
            if (missing is not null)
            {
                Require(operation.Outcome == "not-attempted" && operation.Cause == missing.Value.Cause &&
                    diagnostic.Detail == missing.Value.Detail && operation.Output is null, "Required causal not-attempted result changed.");
                continue;
            }
            Require(operation.Outcome is "succeeded" or "failed", "Eligible operation must have an attempted terminal outcome.");
            Require(operation.Id != "input.validate" || operation.Outcome == "succeeded", "Input identity must succeed before sealing.");
            if (operation.Output is { } artifact) references.Add(artifact);
            if (operation.Id == "source.resolve")
            {
                Require(operation.Output is not null, "Attempted source resolution must retain its layout/candidates.");
                var layout = PreparationJson.Parse<SourceLayout>(Read(root, operation.Output!));
                Require(PreparationJson.Same(layout, SourceLayoutResolver.Resolve(inventory!, input, receipt.Request.SourceRootId)) &&
                    operation.Outcome == layout.Outcome && operation.Cause == layout.Cause, "Root layout/candidates do not match full inventory and exact arguments.");
                continue;
            }
            if (operation.Outcome == "failed")
            {
                Require(operation.Cause == "collector-failure" && operation.Output is null, "Failed collector cannot offer a successful output.");
                continue;
            }
            Require(operation.Cause == "completed", "Successful operation cause must be completed.");
            if (operation.Id == "input.validate")
            {
                Require(operation.Output is null, "Input validation has no evidence output.");
                continue;
            }
            Require(operation.Output is not null, "Successful collector output is missing.");
            var bytes = Read(root, operation.Output!);
            switch (operation.Id)
            {
                case "package.inspect":
                    Require(bytes.AsSpan().SequenceEqual(NupkgInspector.SerializeInspection(
                        NupkgInspector.Inspect(FullPath(root, input.Package.NupkgPath, true)))), "Package facts differ from current target.");
                    break;
                case "source.inventory":
                    inventory = PreparationJson.Parse<FullSourceInventory>(bytes);
                    var actual = SourceArchiveCaptureService.InventoryFullArchive(root, receipt.Request.SourceArchive!,
                        receipt.Request.ArchiveFormat!, roles.Single(role => role.Role == "source-archive").Artifact.Sha256);
                    Require(PreparationJson.Same(inventory, actual), "Full inventory accounting differs from the bound archive.");
                    break;
                case "release.collect":
                    ValidateRelease(PreparationJson.Parse<ReleaseFactsResult>(bytes), receipt, roles, root);
                    break;
            }
        }
        var map = PreparationJson.Parse<PreparationRegistrationMap>(Read(root, receipt.RegistrationMap));
        Require(map.SchemaVersion == 1 && map.GenerationId == receipt.GenerationId &&
            PreparationJson.Same(map.Subject, subject) && map.PreInputSha256 == receipt.PreInput.Sha256,
            "Registration map subject/generation/pre-input changed.");
        var eligible = receipt.Operations.Where(op => op.Outcome == "succeeded" && Kind(op.Id) is not null).ToArray();
        Require(map.Entries.Count == eligible.Length, "Each eligible successful output requires exactly one mapping.");
        for (var index = 0; index < eligible.Length; index++)
        {
            var operation = eligible[index];
            var mapping = map.Entries[index];
            var basename = RetainedName(receipt.GenerationId, operation.Id);
            Require(mapping.GenerationId == receipt.GenerationId && mapping.OperationId == operation.Id &&
                mapping.OutputId == "facts" && mapping.Kind == Kind(operation.Id) && mapping.Basename == basename &&
                mapping.Retained.Path == basename && PreparationJson.Same(mapping.Original, operation.Output!),
                "Missing, duplicate, failed-output or cross-generation registration mapping.");
            Require(mapping.Original.Size == mapping.Retained.Size && mapping.Original.Sha256 == mapping.Retained.Sha256 &&
                Read(root, mapping.Original).AsSpan().SequenceEqual(Read(root, mapping.Retained)), "Retention copy changed.");
        }
        var expectedFiles = references.Select(reference => FullPath(root, reference.Path, true)).ToHashSet(StringComparer.Ordinal);
        expectedFiles.Add(FullPath(root, receipt.Request.Output + "/preparation.receipt.json", false));
        var generationPath = FullPath(root, receipt.Request.Output, false);
        Require(!Directory.EnumerateDirectories(generationPath).Any() &&
            Directory.EnumerateFiles(generationPath).All(expectedFiles.Contains), "Generation contains unreferenced artifacts.");
        // Recheck exact bound bytes at the seal/validation boundary after collector validation.
        foreach (var reference in references.Concat(map.Entries.Select(entry => entry.Retained)).Append(receipt.PreInput).Append(receipt.Reservation))
            _ = Read(root, reference);
        InputManifestService.Validate(input, root, requireConfirmed: true);
        return (input, map);
    }

    private static void ValidateRelease(ReleaseFactsResult result, PreparationReceipt receipt, PreparationRole[] roles, string root)
    {
        Require(result.SchemaVersion == 1 && result.ProducerVersion == "release-facts/1.0.0" && result.NormalizationVersion == 1 &&
            result.InputManifest.Path == receipt.Request.Input && result.InputManifest.Sha256 == receipt.PreInput.Sha256 &&
            result.Execution.Root == root, "Release output input/producer binding mismatch.");
        var expected = roles.Where(role => role.Role != "source-archive").Select(role => new ReleaseRole(role.Role,
            role.ManifestPointer, role.Artifact.Path, role.Artifact.Size, role.Artifact.Sha256)).ToArray();
        Require(result.Roles.Count == expected.Length && expected.All(role =>
            result.Roles.Count(actual => PreparationJson.Same(actual, role)) == 1), "Release role binding mismatch.");
        foreach (var (role, entry) in new[] { ("spdx22", receipt.Request.Spdx22Entry), ("spdx30", receipt.Request.Spdx30Entry) })
        {
            var locatedFacts = result.Facts.Where(fact => fact.Source.Role == role).ToArray();
            Require(locatedFacts.Length > 0 && locatedFacts.All(fact => fact.Source.Container == entry),
                "Release SPDX fact containers differ from selected entries.");
        }
        Require(result.Operations.Select(op => op.Id).SequenceEqual(new[]
            { "validate-inputs", "bind-roles", "target", "release-package", "spdx22", "spdx30", "provenance", "sbom-statement", "compare", "publish" }) &&
            result.Operations.All(op => op.Outcome == "succeeded"), "Release facts include an unsuccessful required operation.");
        Require(result.Facts.Count > 0 && result.Facts.Select(fact => fact.Id).Distinct(StringComparer.Ordinal).Count() == result.Facts.Count &&
            result.Comparisons.Select(comparison => comparison.Id).SequenceEqual(new[]
            {
                "target-release", "spdx22-target", "spdx22-release", "spdx30-target", "spdx30-release",
                "spdx30-release-literal", "provenance-target", "provenance-release", "sbom-target", "sbom-release", "sbom-document"
            }) &&
            result.Comparisons.All(comparison => comparison.Outcome is "match" or "mismatch" or "not-comparable" &&
                result.Facts.Contains(comparison.Left) && result.Facts.Contains(comparison.Right)) &&
            result.Facts.All(fact => !string.IsNullOrEmpty(fact.Id) && !string.IsNullOrEmpty(fact.Kind) &&
                roles.Any(role => role.Role == fact.Source.Role)),
            "Release facts have invalid fact/comparison accounting.");
        var accounting = result.Accounting;
        long Number(string value)
        {
            Require(long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
                number >= 0 && number <= ResourceLimits.SupplementalInputAggregateBytes &&
                number.ToString("D12", CultureInfo.InvariantCulture) == value, "Invalid release byte accounting.");
            return number;
        }
        Require(Number(accounting.LimitBytes) == ResourceLimits.SupplementalInputAggregateBytes &&
            Number(accounting.SelectedRawBytes) == roles.Where(role => role.Role is not ("target" or "source-archive")).Sum(role => role.Artifact.Size) &&
            Number(accounting.ResultBytes) == result.Serialize().LongLength && Number(accounting.DiagnosticsBytes) == 0 &&
            Number(accounting.TotalBytes) == Number(accounting.SelectedRawBytes) + Number(accounting.ExpandedBytes) +
                Number(accounting.DecodedBytes) + Number(accounting.ResultBytes), "Release collective accounting mismatch.");
    }

    private static DateTimeOffset Time(string value)
    {
        Require(DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) &&
            time.Offset == TimeSpan.Zero && time.ToString("O", CultureInfo.InvariantCulture) == value, "Invalid preparation UTC execution timestamp.");
        return time;
    }
}
