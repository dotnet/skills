using System.Globalization;
using System.IO.Compression;
using System.Text;
using BlazorPackageResolver;

internal static class Lp08BoundaryChecks
{
    private const string NuGetNamespace = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
    private const string Blank = "Authors is not populated; the authors element is empty or whitespace-only.";
    private const string Delimiters =
        "Authors is not populated; the authors text has no nonblank comma-separated entry.";
    private const string Foreign = "A foreign-namespace authors element cannot establish NuGet Authors.";
    private const string Malformed = "The authors element has unsupported attributes or child elements.";
    private const string Conflicting = "Conflicting authors elements prevent an unambiguous Authors value.";
    private const string Multiple = "Multiple authors elements prevent an unambiguous Authors value.";

    internal static void Run(Registry registry)
    {
        Probe(registry, "em-space-only", Manifest("<authors>\u2003</authors>"),
            "blank-authors", Blank);
        Probe(registry, "unicode-padding", Manifest("<authors>\u00a0 Ada \u2003</authors>"),
            "populated", "Authors is populated: Ada.");
        Probe(registry, "unicode-delimiters", Manifest("<authors>\u2003,\u00a0,\u2003</authors>"),
            "delimiter-only", Delimiters);
        Probe(registry, "prefixed-recognized",
            Manifest($"<n:authors xmlns:n=\"{NuGetNamespace}\">Ada</n:authors>", NuGetNamespace),
            "populated", "Authors is populated: Ada.");
        Probe(registry, "prefixed-other-recognized",
            Manifest("<n:authors xmlns:n=\"http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd\">" +
                "Ada</n:authors>", NuGetNamespace),
            "foreign-authors", null, Foreign, "recognized-authors-metadata");
        Probe(registry, "reset-default-namespace",
            Manifest("<authors xmlns=\"\">Ada</authors>", NuGetNamespace),
            "foreign-authors", null, Foreign, "recognized-authors-metadata");
        Probe(registry, "case-sensitive-duplicates",
            Manifest("<authors>Ada</authors><authors>ADA</authors>"),
            "ambiguous", null, Conflicting, "unambiguous-nuspec");
        Probe(registry, "padded-duplicates",
            Manifest("<authors>Ada</authors><authors> \u00a0Ada\u2003 </authors>"),
            "ambiguous", null, Multiple, "unambiguous-nuspec");
        Probe(registry, "unicode-form-duplicates",
            Manifest("<authors>Caf\u00e9</authors><authors>Cafe\u0301</authors>"),
            "ambiguous", null, Conflicting, "unambiguous-nuspec");
        Probe(registry, "attribute-before-blank", Manifest("<authors extra=\"x\"> </authors>"),
            "malformed-authors", null, Malformed, "corrected-nuspec");
        Probe(registry, "namespaced-attribute-before-delimiter",
            Manifest("<authors xmlns:x=\"urn:other\" x:extra=\"x\"> , </authors>"),
            "malformed-authors", null, Malformed, "corrected-nuspec");
        Probe(registry, "child-before-blank", Manifest("<authors><name /></authors>"),
            "malformed-authors", null, Malformed, "corrected-nuspec");
        Probe(registry, "child-before-delimiter",
            Manifest("<authors>,<name /> , </authors>"),
            "malformed-authors", null, Malformed, "corrected-nuspec");
        Probe(registry, "unrelated-foreign-sibling",
            Manifest("<other:hint xmlns:other=\"urn:other\">ignored</other:hint><authors>Ada</authors>"),
            "populated", "Authors is populated: Ada.");
        Probe(registry, "decomposed-unicode", Manifest("<authors>Cafe\u0301</authors>"),
            "populated", "Authors is populated: Cafe\u0301.");
        Probe(registry, "foreign-before-duplicate",
            Manifest("<authors>Ada</authors><authors>Ada</authors>" +
                "<x:authors xmlns:x=\"urn:other\">Ada</x:authors>"),
            "foreign-authors", null, Foreign, "recognized-authors-metadata");
        Probe(registry, "nested-authors", Manifest("<container><authors>Ada</authors></container>"),
            "missing-authors", "Authors is not populated; the nuspec has no direct authors element.");
        Probe(registry, "outside-metadata",
            "<package><metadata /><authors>Ada</authors></package>",
            "missing-authors", "Authors is not populated; the nuspec has no direct authors element.");
        Probe(registry, "foreign-metadata",
            "<package><other:metadata xmlns:other=\"urn:other\"><other:authors>Ada</other:authors>" +
                "</other:metadata></package>",
            "invalid-xml", null, "The nuspec requires exactly one recognized metadata element.",
            "valid-nuspec", "unknown");

        var oldCulture = CultureInfo.CurrentCulture;
        var oldUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var culture in new[] { "tr-TR", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                Probe(registry, $"case-sensitive-{culture}",
                    Manifest("<authors>Ada</authors><authors>ADA</authors>"),
                    "ambiguous", null, Conflicting, "unambiguous-nuspec");
                Probe(registry, $"decomposed-{culture}", Manifest("<authors>Cafe\u0301</authors>"),
                    "populated", "Authors is populated: Cafe\u0301.");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUiCulture;
        }
        Console.WriteLine("LP-08 supplemental metadata boundary checks: 23 cases passed.");
    }

    internal static void ProbeResourceBound(Registry registry, string name, byte[] bytes, string branch)
    {
        var missing = branch == "unsafe-nuspec"
            ? "The nuspec exceeds the safe inspection size limit."
            : "The supplied checked bytes are not a readable NuGet archive.";
        ProbeBytes(registry, name, bytes, branch, null, missing,
            branch == "unsafe-nuspec" ? "safe-nuspec" : "valid-nupkg", "unknown");
    }

    private static string Manifest(string content, string? namespaceName = null)
    {
        var attribute = namespaceName is null ? "" : $" xmlns=\"{namespaceName}\"";
        return $"<package{attribute}><metadata>{content}</metadata></package>";
    }

    private static void Probe(Registry registry, string name, string xml, string branch,
        string? finding, string? missing = null, string? required = null, string applicability = "applicable")
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        using (var stream = zip.CreateEntry("Sample.nuspec", CompressionLevel.NoCompression).Open())
            stream.Write(Encoding.UTF8.GetBytes(xml));
        ProbeBytes(registry, name, buffer.ToArray(), branch, finding, missing, required, applicability);
    }

    private static void ProbeBytes(Registry registry, string name, byte[] bytes, string branch,
        string? finding, string? missing, string? required, string applicability)
    {
        var pinned = new PinnedInput("package", $"nonexistent/lp08-{name}.nupkg", Catalog.Digest(bytes));
        var input = new ResolverCase(name, "LP-08", [pinned],
            new Dictionary<string, CheckedInput> { ["package"] = new(pinned, bytes) });
        var result = registry.Dispatch(input);
        Check(result.RowId == "LP-08" && result.ResolverId == "lp08-authors" &&
            result.ResolverVersion == "1.0.0" && result.BranchId == branch &&
            result.Inputs.SequenceEqual(input.Inputs) && result.Applicability == applicability &&
            result.EvidenceContract is null &&
            Canonical.Write(result.Write).SequenceEqual(Canonical.Write(registry.Dispatch(input).Write)),
            $"{name}: LP-08 branch, identity or repeated bytes differ.");
        if (finding is not null)
            Check(result.Decision == "deterministic" && result.Finding == finding &&
                result.MissingFact is null && result.RequiredInput is null && result.NextCategory is null &&
                result.EvidenceReferences.SequenceEqual([
                    branch == "missing-authors" ? "package:nuspec/metadata" : "package:nuspec/authors"
                ]), $"{name}: LP-08 factual finding differs.");
        else
            Check(result.Decision == "escalated" && result.Finding is null &&
                result.MissingFact == missing && result.RequiredInput == required &&
                result.NextCategory == "package-owner" && result.EvidenceReferences.Count == 0,
                $"{name}: LP-08 escalation differs.");
    }

    private static void Check(bool success, string message)
    {
        if (!success)
            throw new Exception(message);
    }
}
