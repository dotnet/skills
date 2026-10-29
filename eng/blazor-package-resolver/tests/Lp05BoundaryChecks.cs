using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using BlazorPackageResolver;

internal static class Lp05BoundaryChecks
{
    private const int Mib = 1024 * 1024;
    private const string NuspecStart = "<package><metadata><id>Sample.LP05</id><version>1.0.0</version>";
    private const string NuspecEnd = "</metadata></package>";
    private const string Expression = "PackageLicenseExpression is populated: MIT.";

    internal static void Run(Registry registry)
    {
        var total = Stopwatch.StartNew();
        foreach (var (version, branch) in new[]
        {
            ("1", "malformed"), ("1.0", "expression"), ("0.9", "expression"),
            ("1.0.0", "expression"), ("invalid", "malformed"),
            ("1.0.1", "unsupported-version"), ("2.0.0", "unsupported-version")
        })
            Probe(registry, $"version-{version}", Package($"<license type=\"expression\" version=\"{version}\">MIT</license>"),
                branch);
        foreach (var (type, branch) in new[]
        {
            ("EXPRESSION", "expression"), ("FiLe", "file"), ("URL", "malformed"),
            ("0", "malformed"), ("1", "malformed"), ("2", "malformed")
        })
            Probe(registry, $"type-{type}", Package($"<license type=\"{type}\">MIT</license>"), branch);
        Probe(registry, "namespace-declaration",
            Package("<license xmlns:hint=\"urn:harmless\" type=\"expression\">MIT</license>"), "expression");
        Probe(registry, "unknown-attribute",
            Package("<license type=\"expression\" extra=\"unknown\">MIT</license>"), "malformed");

        var oversizedPackage = Zip(archive =>
        {
            Add(archive, "Sample.LP05.nuspec",
                NuspecStart + "<license type=\"expression\">MIT</license>" + NuspecEnd);
            using var asset = archive.CreateEntry("assets/large", CompressionLevel.NoCompression).Open();
            var chunk = new byte[8192];
            for (var written = 0; written < 64 * Mib; written += chunk.Length)
                asset.Write(chunk);
        }, 64 * Mib + 4096);
        using (var validArchive = new ZipArchive(new MemoryStream(oversizedPackage), ZipArchiveMode.Read))
            Check(oversizedPackage.Length > 64 * Mib && validArchive.Entries.Count == 2,
                "Package-size probe must be a readable ZIP above 64 MiB.");
        Probe(registry, "over-64mib-package", oversizedPackage, "unreadable-package");
        var tooManyEntries = Zip(archive =>
        {
            Add(archive, "Sample.LP05.nuspec", NuspecStart + "<license type=\"expression\">MIT</license>" + NuspecEnd);
            for (var index = 0; index < 4096; index++)
                Add(archive, $"assets/{index:D4}", "");
        });
        using (var validArchive = new ZipArchive(new MemoryStream(tooManyEntries), ZipArchiveMode.Read))
            Check(validArchive.Entries.Count == 4097, "Entry-count probe must contain 4097 valid entries.");
        Probe(registry, "over-4096-entries", tooManyEntries, "unreadable-package");
        var oversizedDirectory = Zip(archive =>
        {
            Add(archive, "Sample.LP05.nuspec", NuspecStart + NuspecEnd);
            for (var index = 0; index < 129; index++)
                Add(archive, $"assets/{index:D4}", "", new string('x', 65535));
        });
        Check(DirectorySize(oversizedDirectory) > 8 * Mib, "Central-directory probe must cross 8 MiB.");
        using (var validArchive = new ZipArchive(new MemoryStream(oversizedDirectory), ZipArchiveMode.Read))
            Check(validArchive.Entries.Count == 130, "Directory-size probe must be a readable ZIP.");
        Probe(registry, "over-8mib-directory", oversizedDirectory, "unreadable-package");
        var oversizedName = Zip(archive =>
        {
            Add(archive, "Sample.LP05.nuspec", NuspecStart + NuspecEnd);
            Add(archive, new string('x', 513), "");
        });
        using (var validArchive = new ZipArchive(new MemoryStream(oversizedName), ZipArchiveMode.Read))
            Check(validArchive.Entries.Count == 2 && validArchive.Entries[1].FullName.Length == 513,
                "Entry-name probe must be a readable ZIP with a 513-character name.");
        Probe(registry, "over-512-character-entry-name", oversizedName, "unreadable-package");
        var oversizedNuspec = Package("<description>" + new string('x', Mib) + "</description>");
        using (var validArchive = new ZipArchive(new MemoryStream(oversizedNuspec), ZipArchiveMode.Read))
            Check(validArchive.Entries.Single().Length > Mib, "Nuspec probe must exceed 1 MiB.");
        Probe(registry, "over-1mib-nuspec", oversizedNuspec, "unsafe-nuspec");

        var nearLimitDirectory = Zip(archive =>
        {
            Add(archive, "Sample.LP05.nuspec", NuspecStart + NuspecEnd);
            for (var index = 0; index < 127; index++)
                Add(archive, $"assets/{index:D4}", "", new string('x', 65535));
        });
        Check(DirectorySize(nearLimitDirectory) < 8 * Mib &&
            DirectorySize(nearLimitDirectory) > 7 * Mib, "Near-limit directory probe must remain below 8 MiB.");
        var truncatedDirectory = new byte[nearLimitDirectory.Length - 1];
        Array.Copy(nearLimitDirectory, truncatedDirectory, truncatedDirectory.Length - 22);
        Array.Copy(nearLimitDirectory, nearLimitDirectory.Length - 22,
            truncatedDirectory, truncatedDirectory.Length - 22, 22);
        Probe(registry, "truncated-near-limit-directory", truncatedDirectory, "unreadable-package");

        var nearLimitName = Zip(archive =>
        {
            Add(archive, "Sample.LP05.nuspec", NuspecStart + NuspecEnd);
            Add(archive, new string('x', 512), "");
        });
        var directoryStart = (int)BinaryPrimitives.ReadUInt32LittleEndian(
            nearLimitName.AsSpan(nearLimitName.Length - 22 + 16, 4));
        var firstNameLength = BinaryPrimitives.ReadUInt16LittleEndian(nearLimitName.AsSpan(directoryStart + 28, 2));
        var secondHeader = directoryStart + 46 + firstNameLength;
        Check(BinaryPrimitives.ReadUInt32LittleEndian(nearLimitName.AsSpan(secondHeader, 4)) == 0x02014b50,
            "Near-limit name probe must mutate the second central-directory entry.");
        BinaryPrimitives.WriteUInt16LittleEndian(nearLimitName.AsSpan(secondHeader + 28, 2), 513);
        Probe(registry, "truncated-near-limit-entry-name", nearLimitName, "unreadable-package");

        var nearLimitCount = (byte[])tooManyEntries.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(nearLimitCount.AsSpan(nearLimitCount.Length - 22 + 8, 2), 4096);
        BinaryPrimitives.WriteUInt16LittleEndian(nearLimitCount.AsSpan(nearLimitCount.Length - 22 + 10, 2), 4096);
        Probe(registry, "truncated-near-limit-entry-count", nearLimitCount, "unreadable-package");
        Console.WriteLine($"LP-05 boundary and attribute checks: {total.Elapsed.TotalMilliseconds:F0} ms total.");
    }

    private static byte[] Package(string licenseMetadata) =>
        Zip(archive => Add(archive, "Sample.LP05.nuspec", NuspecStart + licenseMetadata + NuspecEnd));

    private static byte[] Zip(Action<ZipArchive> populate, int capacity = 0)
    {
        using var buffer = new MemoryStream(capacity);
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            populate(archive);
        return buffer.ToArray();
    }

    private static void Add(ZipArchive archive, string name, string content, string? comment = null)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        if (comment is not null)
            entry.Comment = comment;
        using var output = entry.Open();
        output.Write(Encoding.UTF8.GetBytes(content));
    }

    private static uint DirectorySize(byte[] bytes) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 22 + 12, 4));

    private static void Probe(Registry registry, string name, byte[] bytes, string branch)
    {
        if (name.StartsWith("over-", StringComparison.Ordinal) ||
            name.StartsWith("truncated-", StringComparison.Ordinal))
        {
            Lp06BoundaryChecks.ProbeResourceBound(registry, name, bytes, branch);
            Lp07BoundaryChecks.ProbeResourceBound(registry, name, bytes, branch);
            Lp08BoundaryChecks.ProbeResourceBound(registry, name, bytes, branch);
            Lp09BoundaryChecks.ProbeResourceBound(registry, name, bytes, branch);
        }
        var timer = Stopwatch.StartNew();
        var pinned = new PinnedInput("package", $"in-memory/{name}.nupkg", Catalog.Digest(bytes));
        var input = new ResolverCase(name, "LP-05", [pinned],
            new Dictionary<string, CheckedInput> { ["package"] = new(pinned, bytes) });
        var result = registry.Dispatch(input);
        var second = registry.Dispatch(input);
        Check(Canonical.Write(result.Write).SequenceEqual(Canonical.Write(second.Write)),
            $"{name}: output bytes changed between identical checked inputs.");
        Check(result.BranchId == branch && result.Inputs.SequenceEqual(input.Inputs) &&
            result.ResolverId == "lp05-license-expression" && result.ResolverVersion == "1.0.0",
            $"{name}: unexpected branch or input identity.");
        if (branch == "expression")
            Check(result.Decision == "deterministic" && result.Finding == Expression &&
                result.EvidenceReferences.SequenceEqual(["package:nuspec/license"]),
                $"{name}: expression finding or evidence differs.");
        else if (branch == "file")
            Check(result.Decision == "deterministic" &&
                result.Finding == "PackageLicenseExpression is not populated; the nuspec declares a license file.",
                $"{name}: file finding differs.");
        else
            Check(result.Decision == "escalated" && result.Applicability ==
                (branch == "malformed" || branch == "unsupported-version" ? "applicable" : "unknown") &&
                result.Finding is null && result.EvidenceReferences.Count == 0 &&
                result.EvidenceContract is null && result.NextCategory == "package-owner" &&
                result.MissingFact == (branch switch
                {
                    "malformed" when name.StartsWith("type-", StringComparison.Ordinal) =>
                        "The license type is not a supported NuGet expression or file type.",
                    "malformed" => "A nonblank license expression cannot be established from malformed metadata.",
                    "unsupported-version" => "The license expression version is newer than supported 1.0.0.",
                    "unsafe-nuspec" => "The nuspec exceeds the safe inspection size limit.",
                    _ => "The supplied checked bytes are not a readable NuGet archive."
                }) &&
                result.RequiredInput == (branch switch
                {
                    "malformed" => "corrected-nuspec",
                    "unsupported-version" => "supported-license-version",
                    "unsafe-nuspec" => "safe-nuspec",
                    _ => "valid-nupkg"
                }), $"{name}: escalation fields differ.");
        Console.WriteLine($"LP-05 {name}: {branch}; {bytes.Length} bytes; {timer.Elapsed.TotalMilliseconds:F0} ms (two dispatches).");
    }

    private static void Check(bool success, string message)
    {
        if (!success)
            throw new Exception(message);
    }
}
