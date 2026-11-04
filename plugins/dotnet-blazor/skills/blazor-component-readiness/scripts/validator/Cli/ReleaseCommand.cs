using BlazorComponentReadiness.Validator.Release;

namespace BlazorComponentReadiness.Validator.Cli;

internal static class ReleaseCommand
{
    private const string Help =
        """
        Collect bounded, offline release facts from confirmed local inputs.

        Usage:
          readiness-validator release facts --root <approved-root> --input <confirmed-manifest>
            --release-package <registered-basename>
            --spdx22 <registered-basename> --spdx22-entry <exact-zip-entry>
            --spdx30 <registered-basename> --spdx30-entry <exact-zip-entry>
            --provenance <registered-basename> --sbom-statement <registered-basename>
            --output <new-file>

        Input/output paths are relative to root (or absolute beneath it). The target
        nupkg comes only from the confirmed manifest. Five distinct supplemental roles
        select existing evidence_inputs or owner_inputs; no replacement hashes.
        Supports UTF-8 SPDX-2.2 JSON, the literal SPDX3 3.0.1-context graph shape,
        Sigstore v0.3 DSSE in-toto Statement/v1 with SLSA provenance/v1 or SPDX2.2.
        No network, context fetching, signature authentication, readiness conclusions
        or requirement IDs. PackageVerificationCode is not a file checksum.
        Complete comparisons may match, mismatch or be not-comparable; malformed,
        ambiguous, changed, unsafe or over-limit inputs fail without publishing.
        Output must be new. All required operations are accounted for; failures
        report succeeded/failed/not-attempted operations on stderr.
        For evidence handoff, preserve this pre-output manifest. Register the
        derived result via inputs candidates add-evidence, discover a new draft,
        explicitly confirm it, then initialize/export the final identity. Use
        evidence draft-add registered mode and evidence bundle --root/--manifest
        input-bound mode; see references/artifact-acquisition.md.
        """;

    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        if (args.Count == 0 || args[0] is "--help" or "-h" ||
            (args.Count == 2 && args[0] == "facts" && args[1] is "--help" or "-h"))
        {
            output.WriteLine(Help);
            return ExitCodes.Success;
        }

        if (args[0] != "facts")
        {
            throw new UsageException($"Unknown release command '{args[0]}'.");
        }

        var options = CommandOptions.Parse(args.Skip(1).ToArray(),
            "--root", "--input", "--release-package", "--spdx22", "--spdx22-entry",
            "--spdx30", "--spdx30-entry", "--provenance", "--sbom-statement", "--output");
        _ = ReleaseFactsService.Collect(
            options.Single("--root"), options.Single("--input"),
            options.Single("--release-package"), options.Single("--spdx22"),
            options.Single("--spdx22-entry"), options.Single("--spdx30"),
            options.Single("--spdx30-entry"), options.Single("--provenance"),
            options.Single("--sbom-statement"), options.Single("--output"));
        return ExitCodes.Success;
    }
}
