using System.Globalization;
using System.IO.Compression;
using System.Text;
using BlazorPackageResolver;

internal static class Lp09BoundaryChecks
{
    private const string NuGetNamespace = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
    private const string OtherNuGetNamespace = "http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd";
    private const string Variant =
        "A case-variant ProjectUrl has conflicting interpretation: the schema and ManifestReader require exact projectUrl, but case-insensitive NuGet metadata readers recognize ProjectUrl.";
    private const string MixedVariant =
        "An exact projectUrl beside a case-variant ProjectUrl has conflicting interpretation: the schema and ManifestReader require exact projectUrl, but case-insensitive NuGet metadata readers recognize both.";
    private const string Foreign =
        "A foreign-namespace projectUrl element cannot establish NuGet ProjectUrl.";
    private const string Multiple =
        "Multiple projectUrl elements prevent an unambiguous ProjectUrl value.";
    private const string Conflicting =
        "Conflicting projectUrl elements prevent an unambiguous ProjectUrl value.";
    private const string Malformed =
        "The projectUrl element has unsupported attributes or child elements.";

    internal static void Run(Registry registry)
    {
        var probes = 0;
        void CheckField(string name, string xml, string branch, string? finding,
            string? missing = null, string? required = null, string applicability = "applicable")
        {
            Probe(registry, name, Package(xml), branch, finding, missing, required, applicability);
            probes++;
        }

        CheckField("lowercase-variant", Manifest("<projecturl>value</projecturl>"),
            "case-variant-project-url", null, Variant, "corrected-nuspec");
        CheckField("uppercase-variant", Manifest("<PROJECTURL>value</PROJECTURL>"),
            "case-variant-project-url", null, Variant, "corrected-nuspec");
        CheckField("foreign-variant", Manifest("<x:ProjectUrl xmlns:x=\"urn:other\">value</x:ProjectUrl>"),
            "case-variant-project-url", null, Variant, "corrected-nuspec");
        CheckField("foreign-variant-with-exact",
            Manifest("<projectUrl>value</projectUrl><x:ProjectUrl xmlns:x=\"urn:other\">other</x:ProjectUrl>"),
            "case-variant-project-url", null, MixedVariant, "corrected-nuspec");
        CheckField("all-candidate-faults",
            Manifest("<projectUrl extra=\"x\"><child /></projectUrl><projectUrl>different</projectUrl>" +
                "<x:projectUrl xmlns:x=\"urn:other\">foreign</x:projectUrl><PROJECTURL>variant</PROJECTURL>"),
            "case-variant-project-url", null, MixedVariant, "corrected-nuspec");
        CheckField("foreign-before-duplicate-and-shape",
            Manifest("<projectUrl extra=\"x\"/><projectUrl>different</projectUrl>" +
                "<x:projectUrl xmlns:x=\"urn:other\">foreign</x:projectUrl>"),
            "foreign-project-url", null, Foreign, "recognized-project-url-metadata");
        CheckField("duplicate-before-shape",
            Manifest("<projectUrl extra=\"x\">same</projectUrl><projectUrl>different</projectUrl>"),
            "ambiguous", null, Conflicting, "unambiguous-nuspec");
        CheckField("duplicate-normalizes-trimmed-text",
            Manifest("<projectUrl> &#9;value&#10; </projectUrl><projectUrl>value</projectUrl>"),
            "ambiguous", null, Multiple, "unambiguous-nuspec");
        CheckField("shape-before-blank", Manifest("<projectUrl extra=\"x\"> </projectUrl>"),
            "malformed-project-url", null, Malformed, "corrected-nuspec");
        CheckField("missing-nested-only", Manifest("<container><projectUrl>value</projectUrl></container>"),
            "missing-project-url",
            "ProjectUrl is not populated; the nuspec metadata has no direct projectUrl element.");
        CheckField("invalid-uri-is-text", Manifest("<projectUrl>https://[invalid</projectUrl>"),
            "populated", "ProjectUrl is populated: https://[invalid.");
        Check(!Uri.TryCreate("https://[invalid", UriKind.RelativeOrAbsolute, out _),
            "The invalid-URI probe must be rejected by Uri.TryCreate.");
        CheckField("same-namespace-prefix",
            Manifest($"<x:projectUrl xmlns:x=\"{NuGetNamespace}\">value</x:projectUrl>", NuGetNamespace),
            "populated", "ProjectUrl is populated: value.");
        CheckField("different-recognized-namespace",
            Manifest($"<x:projectUrl xmlns:x=\"{OtherNuGetNamespace}\">value</x:projectUrl>", NuGetNamespace),
            "foreign-project-url", null,
            "A projectUrl in a different recognized NuGet namespace cannot establish ProjectUrl for this metadata.",
            "recognized-project-url-metadata");
        CheckField("unqualified-child",
            Manifest("<projectUrl xmlns=\"\">value</projectUrl>", NuGetNamespace),
            "foreign-project-url", null,
            "An unqualified projectUrl child cannot establish ProjectUrl for namespaced metadata.",
            "recognized-project-url-metadata");
        CheckField("processing-instruction-only",
            Manifest("<projectUrl><?hint value?></projectUrl>"),
            "blank-project-url", "ProjectUrl is not populated; the projectUrl element is empty or whitespace-only.");
        CheckField("processing-instruction-interleaved",
            Manifest("<projectUrl>before<?hint value?>after</projectUrl>"),
            "populated", "ProjectUrl is populated: beforeafter.");
        CheckField("line-endings-normalized",
            Manifest("<projectUrl> \r\nbefore\rinside\r\nafter\n </projectUrl>"),
            "populated", "ProjectUrl is populated: before\ninside\nafter.");
        CheckField("zero-width-space-only", Manifest("<projectUrl>\u200b</projectUrl>"),
            "populated", "ProjectUrl is populated: \u200b.");
        CheckField("byte-order-mark-only", Manifest("<projectUrl>\ufeff</projectUrl>"),
            "populated", "ProjectUrl is populated: \ufeff.");
        CheckField("format-characters-preserved",
            Manifest("<projectUrl>\u00a0\u200bvalue\ufeff\u2003</projectUrl>"),
            "populated", "ProjectUrl is populated: \u200bvalue\ufeff.");
        CheckField("unicode-whitespace-only",
            Manifest("<projectUrl>\u00a0\u2003\u2028</projectUrl>"),
            "blank-project-url", "ProjectUrl is not populated; the projectUrl element is empty or whitespace-only.");
        CheckField("unicode-whitespace-padding",
            Manifest("<projectUrl>\u00a0 \u2003value\u2028 </projectUrl>"),
            "populated", "ProjectUrl is populated: value.");

        var oldCulture = CultureInfo.CurrentCulture;
        var oldUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var culture in new[] { "tr-TR", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                CheckField($"culture-variant-{culture}",
                    Manifest("<projectUrl>exact</projectUrl><PROJECTURL>variant</PROJECTURL>"),
                    "case-variant-project-url", null, MixedVariant, "corrected-nuspec");
                CheckField($"culture-trim-{culture}",
                    Manifest("<projectUrl>\u00a0\u200bvalue\ufeff\u2003</projectUrl>"),
                    "populated", "ProjectUrl is populated: \u200bvalue\ufeff.");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUiCulture;
        }
        Console.WriteLine($"LP-09 supplemental metadata boundary checks: {probes} cases passed.");
    }

    internal static void ProbeResourceBound(Registry registry, string name, byte[] bytes, string branch)
    {
        var missing = branch == "unsafe-nuspec"
            ? "The nuspec exceeds the safe inspection size limit."
            : "The supplied checked bytes are not a readable NuGet archive.";
        Probe(registry, name, bytes, branch, null, missing,
            branch == "unsafe-nuspec" ? "safe-nuspec" : "valid-nupkg", "unknown");
    }

    private static string Manifest(string content, string? namespaceName = null)
    {
        var attribute = namespaceName is null ? "" : $" xmlns=\"{namespaceName}\"";
        return $"<package{attribute}><metadata>{content}</metadata></package>";
    }

    private static byte[] Package(string xml)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        using (var stream = zip.CreateEntry("Sample.nuspec", CompressionLevel.NoCompression).Open())
            stream.Write(Encoding.UTF8.GetBytes(xml));
        return buffer.ToArray();
    }

    private static void Probe(Registry registry, string name, byte[] bytes, string branch,
        string? finding, string? missing, string? required, string applicability)
    {
        var pinned = new PinnedInput("package", $"nonexistent/lp09-{name}.nupkg", Catalog.Digest(bytes));
        var input = new ResolverCase(name, "LP-09", [pinned],
            new Dictionary<string, CheckedInput> { ["package"] = new(pinned, bytes) });
        var result = registry.Dispatch(input);
        Check(result.RowId == "LP-09" && result.ResolverId == "lp09-project-url" &&
            result.ResolverVersion == "1.0.0" && result.BranchId == branch &&
            result.Inputs.SequenceEqual(input.Inputs) && result.Applicability == applicability &&
            result.EvidenceContract is null &&
            Canonical.Write(result.Write).SequenceEqual(Canonical.Write(registry.Dispatch(input).Write)),
            $"{name}: LP-09 branch, identity or repeated bytes differ.");
        if (finding is not null)
            Check(result.Decision == "deterministic" && result.Finding == finding &&
                result.MissingFact is null && result.RequiredInput is null && result.NextCategory is null &&
                result.EvidenceReferences.SequenceEqual([
                    branch == "missing-project-url" ? "package:nuspec/metadata" : "package:nuspec/projectUrl"
                ]), $"{name}: LP-09 factual finding differs.");
        else
            Check(result.Decision == "escalated" && result.Finding is null &&
                result.MissingFact == missing && result.RequiredInput == required &&
                result.NextCategory == "package-owner" && result.EvidenceReferences.Count == 0,
                $"{name}: LP-09 escalation differs.");
    }

    private static void Check(bool success, string message)
    {
        if (!success)
            throw new Exception(message);
    }
}
