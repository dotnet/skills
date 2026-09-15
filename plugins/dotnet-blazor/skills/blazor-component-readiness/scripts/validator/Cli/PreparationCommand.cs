using BlazorComponentReadiness.Validator.Preparation;
using BlazorComponentReadiness.Validator.IO;

namespace BlazorComponentReadiness.Validator.Cli;

public static class PreparationCommand
{
    public const string Help = """
        Operator-invoked package facts and full archive inventory (authorized-package-48/1.0.0)

        package prepare --root <approved-root> --input <confirmed> --output <new-generation-directory>
          [--source-archive <registered-basename> --archive-format <tar.gz|zip> [--source-root-id <root-sha256>]]
          [--release-package <registered-basename>]
          [--spdx22 <registered-basename> --spdx22-entry <exact-entry>]
          [--spdx30 <registered-basename> --spdx30-entry <exact-entry>]
          [--provenance <registered-basename>] [--sbom-statement <registered-basename>]

        Target and the pinned authorized-48 scope come only from confirmed inputs.
        Missing optional roles are not-attempted; broken explicit bindings are fatal.
        Five fixed operations: input.validate, package.inspect, source.inventory,
        source.resolve, release.collect. Independent collectors continue after inner failures.
        Source inventory covers all logical regular files and directories, without extraction.
        Only a bound GitHub codeload repo-fullcommit wrapper is auto-stripped once.
        Unknown layouts retain root candidates; choose a listed ID in a NEW generation.

        Exit 0 means a receipt was sealed for accounting/correspondence only; it may
        contain collector failures or adverse facts. It does not mean readiness verified.
        Preserve the generation and root-level retained copies. No automatic registration,
        confirmation, assessment, source capture, model invocation or report publication.
        Add only selected successful registration-map entries via inputs candidates,
        discover and explicitly confirm a NEW manifest, then validate its subset binding:
          inputs validate --root <root> --manifest <final-confirmed> --preparation <preparation.receipt.json>
        The same validation accepts unchanged pre-inputs (an empty selected subset).
        Never import/resume an interrupted generation or register diagnostics as evidence.
        """;

    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count == 0 || args.Count == 1 && args[0] is "--help" or "-h")
        {
            output.WriteLine(Help);
            return 0;
        }
        var options = CommandOptions.Parse(args, "--root", "--input", "--output", "--source-archive",
            "--archive-format", "--source-root-id", "--release-package", "--spdx22", "--spdx22-entry",
            "--spdx30", "--spdx30-entry", "--provenance", "--sbom-statement");
        var request = new PreparationRequest(options.Single("--input"), options.Single("--output"),
            options.Optional("--source-archive"), options.Optional("--archive-format"), options.Optional("--source-root-id"),
            options.Optional("--release-package"), options.Optional("--spdx22"), options.Optional("--spdx22-entry"),
            options.Optional("--spdx30"), options.Optional("--spdx30-entry"), options.Optional("--provenance"),
            options.Optional("--sbom-statement"));
        ValidateOptions(request);
        var receipt = PreparationService.Prepare(options.Single("--root"), request);
        var inventoryOutput = receipt.Operations.Single(op => op.Id == "source.inventory").Output;
        var layoutOutput = receipt.Operations.Single(op => op.Id == "source.resolve").Output;
        var inventory = inventoryOutput is null ? null : PreparationJson.Parse<FullSourceInventory>(BoundedIO.ReadAllBytes(
            SafePath.ResolveUnderRoot(options.Single("--root"), inventoryOutput.Path, true, true),
            ResourceLimits.SerializedArtifactBytes, "preparation inventory summary"));
        var layout = layoutOutput is null ? null : PreparationJson.Parse<SourceLayout>(BoundedIO.ReadAllBytes(
            SafePath.ResolveUnderRoot(options.Single("--root"), layoutOutput.Path, true, true),
            ResourceLimits.SerializedArtifactBytes, "preparation layout summary"));
        output.WriteLine("Preparation receipt sealed (accounting/correspondence only).");
        output.WriteLine($"Operations: succeeded={receipt.Operations.Count(op => op.Outcome == "succeeded")} " +
            $"failed={receipt.Operations.Count(op => op.Outcome == "failed")} " +
            $"not-attempted={receipt.Operations.Count(op => op.Outcome == "not-attempted")} not-applicable=0");
        if (inventory is not null)
            output.WriteLine($"Source inventory: regular-files={inventory.RegularFiles} directories={inventory.Directories} logical-members={inventory.LogicalMembers}");
        if (layout?.Prefix is { } prefix) output.WriteLine($"Source prefix: {(prefix == "" ? "(archive root)" : prefix)}");
        output.WriteLine($"Receipt: {receipt.Request.Output}/preparation.receipt.json");
        output.WriteLine($"Registrations: {receipt.RegistrationMap.Path}");
        foreach (var op in receipt.Operations.Where(op => op.Outcome != "succeeded"))
            output.WriteLine($"{op.Id}: {op.Outcome} / {op.Cause}; diagnostic: {op.Diagnostic.Path}");
        return 0;
    }

    internal static void ValidateOptions(PreparationRequest request)
    {
        if ((request.SourceArchive is null) != (request.ArchiveFormat is null) ||
            request.SourceRootId is not null && request.SourceArchive is null ||
            (request.Spdx22 is null) != (request.Spdx22Entry is null) ||
            (request.Spdx30 is null) != (request.Spdx30Entry is null))
            throw new UsageException("Source requires archive/format together; root ID requires source. Each SPDX role requires its entry and vice versa.");
        if (request.ArchiveFormat is not null and not ("tar.gz" or "zip"))
            throw new UsageException("--archive-format must be tar.gz or zip.");
    }
}
