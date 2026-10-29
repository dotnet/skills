using System.IO.Compression;
using System.Text;
using BlazorPackageResolver;

internal static class Lp06BoundaryChecks
{
    internal static void Run(Registry registry)
    {
        foreach (var (name, xml, branch, finding) in new[]
        {
            ("outside-metadata",
                "<package><metadata><id>Sample</id></metadata><repository url=\"https://outer.test\" /></package>",
                "missing-repository", "RepositoryUrl is not populated; the nuspec has no repository URL."),
            ("nested-in-metadata",
                "<package><metadata><container><repository url=\"https://nested.test\" /></container></metadata></package>",
                "missing-repository", "RepositoryUrl is not populated; the nuspec has no repository URL."),
            ("duplicate-metadata",
                "<package><metadata><repository url=\"https://first.test\" /></metadata>" +
                "<metadata><repository url=\"https://second.test\" /></metadata></package>",
                "invalid-xml", null),
            ("foreign-metadata",
                "<package><other:metadata xmlns:other=\"urn:other\">" +
                "<other:repository url=\"https://foreign.test\" /></other:metadata></package>",
                "invalid-xml", null),
            ("direct-in-metadata",
                "<package><metadata><repository url=\" ../source \" /></metadata></package>",
                "populated", "RepositoryUrl is populated: ../source."),
            ("nested-in-repository",
                "<package><metadata><repository url=\"https://example.test\"><inner /></repository></metadata></package>",
                "invalid-xml", null)
        })
        {
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            using (var stream = zip.CreateEntry("Sample.nuspec", CompressionLevel.NoCompression).Open())
                stream.Write(Encoding.UTF8.GetBytes(xml));
            var bytes = buffer.ToArray();
            var pinned = new PinnedInput("package", $"nonexistent/{name}.nupkg", Catalog.Digest(bytes));
            var input = new ResolverCase(name, "LP-06", [pinned],
                new Dictionary<string, CheckedInput> { ["package"] = new(pinned, bytes) });
            var actual = registry.Dispatch(input);
            Check(actual.BranchId == branch && actual.Finding == finding &&
                actual.Inputs.SequenceEqual(input.Inputs) &&
                Canonical.Write(actual.Write).SequenceEqual(Canonical.Write(registry.Dispatch(input).Write)),
                $"{name}: LP-06 metadata location or stable output differs.");
            Check(finding is null
                ? actual.Decision == "escalated" && actual.MissingFact is not null
                : actual.Decision == "deterministic" && actual.EvidenceReferences.Count == 1,
                $"{name}: LP-06 finding or escalation contract differs.");
        }
    }

    internal static void ProbeResourceBound(Registry registry, string name, byte[] bytes, string branch)
    {
        var pinned = new PinnedInput("package", $"nonexistent/{name}.nupkg", Catalog.Digest(bytes));
        var input = new ResolverCase(name, "LP-06", [pinned],
            new Dictionary<string, CheckedInput> { ["package"] = new(pinned, bytes) });
        var actual = registry.Dispatch(input);
        Check(actual.BranchId == branch && actual.Decision == "escalated" &&
            actual.Applicability == "unknown" && actual.Finding is null &&
            actual.MissingFact == (branch == "unsafe-nuspec"
                ? "The nuspec exceeds the safe inspection size limit."
                : "The supplied checked bytes are not a readable NuGet archive.") &&
            actual.RequiredInput == (branch == "unsafe-nuspec" ? "safe-nuspec" : "valid-nupkg") &&
            actual.NextCategory == "package-owner" &&
            Canonical.Write(actual.Write).SequenceEqual(Canonical.Write(registry.Dispatch(input).Write)),
            $"{name}: LP-06 resource-bound escalation or deterministic bytes differ.");
    }

    private static void Check(bool success, string message)
    {
        if (!success)
            throw new Exception(message);
    }
}
