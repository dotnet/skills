using System.Globalization;
using System.IO.Compression;
using System.Text;
using BlazorPackageResolver;

internal static class Lp07BoundaryChecks
{
    private const string Sha1 = "0123456789abcdef0123456789abcdef01234567";
    private const string Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string MissingDirectRepository =
        "RepositoryCommit does not identify a commit SHA; the nuspec metadata has no direct repository element.";
    private const string Abbreviated =
        "RepositoryCommit does not identify a full commit SHA; abbreviated object IDs are repository-dependent.";
    private const string WrongLength =
        "RepositoryCommit does not identify a full commit SHA; the object ID has the wrong length.";
    private const string InvalidCharacters =
        "RepositoryCommit does not identify a full commit SHA; the object ID is not ASCII hexadecimal.";

    internal static void Run(Registry registry)
    {
        foreach (var length in Enumerable.Range(1, 70))
        {
            var value = new string('a', length);
            var branch = length switch
            {
                40 => "full-sha1",
                64 => "full-sha256",
                >= 4 and <= 39 or >= 41 and <= 63 => "abbreviated-commit",
                _ => "invalid-length"
            };
            var finding = branch switch
            {
                "full-sha1" => $"RepositoryCommit declares a full SHA-1 commit SHA: {value}.",
                "full-sha256" => $"RepositoryCommit declares a full SHA-256 commit SHA: {value}.",
                "abbreviated-commit" => Abbreviated,
                _ => WrongLength
            };
            Probe(registry, $"length-{length}", Package(Manifest(Repository(value))), branch, finding);
        }

        foreach (var (name, value, branch, finding) in new[]
        {
            ("sha1-upper-padded", $" &#9; {Sha1.ToUpperInvariant()}  ", "full-sha1",
                $"RepositoryCommit declares a full SHA-1 commit SHA: {Sha1}."),
            ("sha256-upper", Sha256.ToUpperInvariant(), "full-sha256",
                $"RepositoryCommit declares a full SHA-256 commit SHA: {Sha256}."),
            ("empty", "", "blank-commit",
                "RepositoryCommit does not identify a commit SHA; the commit attribute is blank."),
            ("blank", " &#9;  ", "blank-commit",
                "RepositoryCommit does not identify a commit SHA; the commit attribute is blank."),
            ("zero-sha1", new string('0', 40), "null-oid",
                "RepositoryCommit does not identify a commit SHA; an all-zero object ID is a null sentinel."),
            ("zero-sha256", new string('0', 64), "null-oid",
                "RepositoryCommit does not identify a commit SHA; an all-zero object ID is a null sentinel."),
            ("nonhex", new string('a', 39) + "g", "invalid-characters", InvalidCharacters),
            ("whitespace-inside", new string('a', 20) + " " + new string('a', 20),
                "invalid-characters", InvalidCharacters),
            ("arabic-digit", new string('a', 39) + "\u0661", "invalid-characters", InvalidCharacters),
            ("fullwidth-letter", new string('a', 39) + "\uff21", "invalid-characters", InvalidCharacters),
            ("dotted-capital-i", new string('a', 39) + "\u0130", "invalid-characters", InvalidCharacters)
        })
            Probe(registry, name, Package(Manifest(Repository(value))), branch, finding);

        var oldCulture = CultureInfo.CurrentCulture;
        var oldUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var culture in new[] { "tr-TR", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                Probe(registry, $"culture-{culture}", Package(Manifest(Repository(Sha1.ToUpperInvariant()))),
                    "full-sha1", $"RepositoryCommit declares a full SHA-1 commit SHA: {Sha1}.");
                Probe(registry, $"culture-invalid-{culture}",
                    Package(Manifest(Repository(new string('a', 39) + "\u0130"))),
                    "invalid-characters", InvalidCharacters);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUiCulture;
        }

        Probe(registry, "no-url", Package(Manifest(Repository(Sha1))), "full-sha1",
            $"RepositoryCommit declares a full SHA-1 commit SHA: {Sha1}.");
        Probe(registry, "missing-commit", Package(Manifest("<repository url=\"../source\" />")),
            "missing-commit", "RepositoryCommit does not identify a commit SHA; the repository has no commit attribute.");
        Probe(registry, "nested-repository", Package(Manifest($"<container>{Repository(Sha1)}</container>")),
            "missing-repository", MissingDirectRepository);
        Probe(registry, "outside-metadata",
            Package($"<package><metadata /><repository commit=\"{Sha1}\" /></package>"),
            "missing-repository", MissingDirectRepository);
        Probe(registry, "fixed-url-conflicting-commits",
            Package(Manifest($"<repository type=\"git\" url=\"../source\" commit=\"{Sha1}\" />" +
                $"<repository type=\"git\" url=\"../source\" commit=\"{Sha256}\" />")),
            "ambiguous", null, "Conflicting repository elements prevent an unambiguous RepositoryCommit.",
            "unambiguous-nuspec");
        Probe(registry, "different-url-same-commit",
            Package(Manifest($"<repository url=\"../first\" commit=\"{Sha1}\" />" +
                $"<repository url=\"../second\" commit=\"  {Sha1.ToUpperInvariant()} &#9; \" />")),
            "ambiguous", null, "Multiple repository elements prevent an unambiguous RepositoryCommit.",
            "unambiguous-nuspec");
        Probe(registry, "invalid-xml", Package("<package><metadata><repository"),
            "invalid-xml", null, "The nuspec XML is not well formed.", "valid-nuspec", "unknown");
        Probe(registry, "foreign-root",
            Package($"<package xmlns=\"urn:other\"><metadata>{Repository(Sha1)}</metadata></package>"),
            "foreign-root", null, "The package root is not in a recognized NuGet namespace.",
            "recognized-nuspec", "unknown");
        Probe(registry, "dtd-entity",
            Package($"<!DOCTYPE package [<!ENTITY hash \"{Sha1}\">]>" +
                "<package><metadata><repository commit=\"&hash;\" /></metadata></package>"),
            "unsafe-nuspec", null,
            "The nuspec contains a DTD/entity declaration and cannot be safely inspected.",
            "safe-nuspec", "unknown");
        Probe(registry, "corrupt-archive", "not-a-nupkg"u8.ToArray(),
            "unreadable-package", null, "The supplied checked bytes are not a readable NuGet archive.",
            "valid-nupkg", "unknown");
    }

    internal static void ProbeResourceBound(Registry registry, string name, byte[] bytes, string branch)
    {
        Probe(registry, $"lp07-{name}", bytes, branch, null,
            branch == "unsafe-nuspec"
                ? "The nuspec exceeds the safe inspection size limit."
                : "The supplied checked bytes are not a readable NuGet archive.",
            branch == "unsafe-nuspec" ? "safe-nuspec" : "valid-nupkg", "unknown");
    }

    private static string Repository(string value) => $"<repository commit=\"{value}\" />";

    private static string Manifest(string metadata) =>
        $"<package><metadata>{metadata}</metadata></package>";

    private static byte[] Package(string xml)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        using (var stream = zip.CreateEntry("Sample.nuspec", CompressionLevel.NoCompression).Open())
            stream.Write(Encoding.UTF8.GetBytes(xml));
        return buffer.ToArray();
    }

    private static CaseResult Probe(Registry registry, string name, byte[] bytes,
        string branch, string? finding, string? missing = null, string? required = null,
        string applicability = "applicable")
    {
        var pinned = new PinnedInput("package", $"nonexistent/{name}.nupkg", Catalog.Digest(bytes));
        var input = new ResolverCase(name, "LP-07", [pinned],
            new Dictionary<string, CheckedInput> { ["package"] = new(pinned, bytes) });
        var result = registry.Dispatch(input);
        Check(result.RowId == "LP-07" && result.ResolverId == "lp07-repository-commit" &&
            result.ResolverVersion == "1.0.0" && result.BranchId == branch &&
            result.Inputs.SequenceEqual(input.Inputs) && result.Applicability == applicability &&
            Canonical.Write(result.Write).SequenceEqual(Canonical.Write(registry.Dispatch(input).Write)),
            $"{name}: LP-07 branch, identity or repeated bytes differ.");
        if (finding is not null)
            Check(result.Decision == "deterministic" && result.Finding == finding &&
                result.MissingFact is null && result.RequiredInput is null && result.NextCategory is null &&
                result.EvidenceReferences.SequenceEqual([
                    branch == "missing-repository" ? "package:nuspec/metadata" : "package:nuspec/repository"
                ]), $"{name}: LP-07 factual finding differs.");
        else
            Check(result.Decision == "escalated" && result.Finding is null &&
                result.MissingFact == missing && result.RequiredInput == required &&
                result.NextCategory == "package-owner" && result.EvidenceReferences.Count == 0,
                $"{name}: LP-07 escalation differs.");
        return result;
    }

    private static void Check(bool success, string message)
    {
        if (!success)
            throw new Exception(message);
    }
}
