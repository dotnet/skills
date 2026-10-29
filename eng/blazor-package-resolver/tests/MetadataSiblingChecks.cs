using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BlazorPackageResolver;

internal static class MetadataSiblingChecks
{
    private const string NuGetNamespace = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    internal static void Run(Registry registry)
    {
        var fields = "<license type=\"expression\">MIT</license>" +
            $"<repository url=\"https://trusted.test/source.git\" commit=\"{Commit}\" />" +
            "<authors>Ada</authors><projectUrl>https://trusted.test/project</projectUrl>";
        var malicious = "<x:license type=\"expression\">BAD</x:license>" +
            $"<x:repository url=\"https://evil.test/source\" commit=\"{new string('f', 40)}\" />" +
            "<x:authors>BAD</x:authors><x:projectUrl>https://evil.test/project</x:projectUrl>";
        var recognized = $"<metadata><id>Sample</id><version>1.0.0</version>{fields}</metadata>";
        var foreign = $"<x:metadata xmlns:x=\"urn:other\">{malicious}</x:metadata>";
        var unqualified = "<metadata xmlns=\"\"><authors>BAD</authors>" +
            "<projectUrl>https://evil.test/project</projectUrl></metadata>";
        var variant = $"<Metadata>{fields}</Metadata>";
        var upperVariant = $"<METADATA>{fields}</METADATA>";
        string PackageXml(string children) => $"<package xmlns=\"{NuGetNamespace}\">{children}</package>";

        var scenarios = new[]
        {
            ("foreign-before", PackageXml(foreign + recognized), "ambiguous-metadata"),
            ("foreign-after", PackageXml(recognized + foreign), "ambiguous-metadata"),
            ("unqualified-before", PackageXml(unqualified + recognized), "ambiguous-metadata"),
            ("unqualified-after", PackageXml(recognized + unqualified), "ambiguous-metadata"),
            ("duplicate-foreign-before", PackageXml(foreign + recognized + recognized), "ambiguous-metadata"),
            ("duplicate-foreign-after", PackageXml(recognized + recognized + foreign), "ambiguous-metadata"),
            ("variant-before", PackageXml(variant + recognized), "recognized"),
            ("variant-after", PackageXml(recognized + variant), "recognized"),
            ("upper-variant-before", PackageXml(upperVariant + recognized), "recognized"),
            ("upper-variant-after", PackageXml(recognized + upperVariant), "recognized"),
            ("recognized-only", PackageXml(recognized), "recognized"),
            ("foreign-only", PackageXml(foreign), "invalid-xml"),
            ("no-metadata", PackageXml(""), "invalid-xml"),
            ("duplicate-recognized", PackageXml(recognized + recognized), "invalid-xml"),
            ("foreign-root", $"<package xmlns=\"urn:foreign\">{recognized}{foreign}</package>", "foreign-root"),
            ("malformed-xml", PackageXml(recognized + foreign)[..^10], "invalid-xml"),
            ("unsafe-dtd", "<!DOCTYPE package [<!ENTITY value \"BAD\">]>" +
                PackageXml(recognized + foreign), "unsafe-nuspec")
        };
        var rows = new[]
        {
            ("LP-05", "lp05-license-expression", "expression",
                "PackageLicenseExpression is populated: MIT.", "package:nuspec/license"),
            ("LP-06", "lp06-repository-url", "populated",
                "RepositoryUrl is populated: https://trusted.test/source.git.", "package:nuspec/repository"),
            ("LP-07", "lp07-repository-commit", "full-sha1",
                $"RepositoryCommit declares a full SHA-1 commit SHA: {Commit}.", "package:nuspec/repository"),
            ("LP-08", "lp08-authors", "populated",
                "Authors is populated: Ada.", "package:nuspec/authors"),
            ("LP-09", "lp09-project-url", "populated",
                "ProjectUrl is populated: https://trusted.test/project.", "package:nuspec/projectUrl")
        };
        var oldCulture = CultureInfo.CurrentCulture;
        var oldUiCulture = CultureInfo.CurrentUICulture;
        var baseline = new Dictionary<(string, string), byte[]>();
        try
        {
            foreach (var (culture, first) in new[]
            {
                (oldCulture, true),
                (CultureInfo.GetCultureInfo("tr-TR"), false),
                (CultureInfo.GetCultureInfo("ar-SA"), false)
            })
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                foreach (var (name, xml, branch) in scenarios)
                {
                    var bytes = Archive(xml);
                    if (first)
                        Check(bytes.SequenceEqual(Archive(xml)),
                            $"{name}: synthetic package bytes changed between runs.");
                    foreach (var (row, resolver, positiveBranch, finding, reference) in rows)
                    {
                        var pinned = new PinnedInput("package", $"in-memory/{name}.nupkg", Catalog.Digest(bytes));
                        var input = new ResolverCase(name, row, [pinned],
                            new Dictionary<string, CheckedInput> { ["package"] = new(pinned, bytes) });
                        var result = registry.Dispatch(input);
                        var output = Canonical.Write(result.Write);
                        Check(output.SequenceEqual(Canonical.Write(registry.Dispatch(input).Write)) &&
                            result.RowId == row && result.ResolverId == resolver &&
                            result.ResolverVersion == "1.0.0" && result.Inputs.SequenceEqual(input.Inputs) &&
                            result.EvidenceContract is null, $"{row}/{name}: unstable result or identity.");
                        if (branch == "ambiguous-metadata")
                            Check(result.BranchId == branch && result.Decision == "escalated" &&
                                result.Applicability == "unknown" && result.Finding is null &&
                                result.EvidenceReferences.Count == 0 &&
                                result.MissingFact == FrozenAmbiguousMetadataMissingFact &&
                                result.RequiredInput == "unambiguous-nuspec" &&
                                result.NextCategory == "package-owner",
                                $"{row}/{name}: cross-reader ambiguity was not escalated.");
                        else if (branch == "recognized")
                            Check(result.BranchId == positiveBranch && result.Decision == "deterministic" &&
                                result.Applicability == "applicable" && result.Finding == finding &&
                                result.EvidenceReferences.SequenceEqual([reference]) &&
                                result.MissingFact is null && result.RequiredInput is null &&
                                result.NextCategory is null,
                                $"{row}/{name}: unrelated case-variant metadata affected the field.");
                        else
                            Check(result.BranchId == branch && result.Decision == "escalated" &&
                                result.Applicability == "unknown" && result.Finding is null &&
                                result.EvidenceReferences.Count == 0,
                                $"{row}/{name}: prior root, XML, or metadata priority changed.");
                        if (first)
                            baseline.Add((name, row), output);
                        else
                            Check(output.SequenceEqual(baseline[(name, row)]),
                                $"{row}/{name}: result bytes changed with culture.");
                    }
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUiCulture;
        }
        var digest = SHA256.HashData(baseline
            .OrderBy(item => item.Key.Item1, StringComparer.Ordinal)
            .ThenBy(item => item.Key.Item2, StringComparer.Ordinal)
            .SelectMany(item => item.Value)
            .ToArray());
        Console.WriteLine($"Shared metadata sibling checks: {scenarios.Length} shapes x {rows.Length} rows x 3 cultures passed; canonical SHA-256 {Convert.ToHexStringLower(digest)}.");
    }

    private const string FrozenAmbiguousMetadataMissingFact =
        "A foreign-namespace metadata sibling makes authoritative NuGet metadata ambiguous across readers.";

    private static byte[] Archive(string xml)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("Sample.nuspec", CompressionLevel.NoCompression);
            entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes(xml));
        }
        return buffer.ToArray();
    }

    private static void Check(bool success, string message)
    {
        if (!success)
            throw new Exception(message);
    }
}
