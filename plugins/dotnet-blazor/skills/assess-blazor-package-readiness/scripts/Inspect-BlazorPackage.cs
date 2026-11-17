#:package NuGet.Packaging@7.3.1
#:package System.Security.Cryptography.Pkcs@10.0.0
#:property PublishAot=false

// Bounded, static package inspection. No entries are extracted and no inspected
// assembly is loaded. A verified row describes only the evidence named in it.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Formats.Asn1;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using NuGet.Packaging;
using NuGet.Packaging.Signing;

return await Inspector.RunAsync(args);

internal static class Inspector
{
    internal const int MaxEntries = 4096;
    internal const int MaxEntryBytes = 64 * 1024 * 1024;
    internal const int MaxTotalBytes = 256 * 1024 * 1024;
    private const int MaxArchiveBytes = MaxTotalBytes + 16 * 1024 * 1024;
    private const string Untested = "not tested";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = 64
    };

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = ParseOptions(args);
            var package = ReadArchive(options["--nupkg"]);
            var nuspec = ReadNuspec(package);
            var rows = new List<Row>();
            var license = LicensePolicy.Evaluate(nuspec.License);
            bool fileLicense = nuspec.LicenseType.Equals("file", StringComparison.OrdinalIgnoreCase);
            rows.Add(new("LP-01", fileLicense ? Untested : license.Approved ? "verified" : "gap", "D",
                fileLicense ? "license-file-needs-review" : license.Approved ? "approved-noncopyleft-expression" : "license-policy-gap",
                fileLicense ? "A license file needs independent legal review." : license.Summary, fileLicense || !license.Approved,
                new { nuspec.License, nuspec.LicenseType, policy = license, licenseFile = fileLicense ? FindEntry(package, nuspec.License)?.Name : null }));
            rows.Add(new("LP-02", Untested, "E", "public-source-correspondence-not-adjudicated",
                "Public source correspondence for the shipped version requires repository and release evidence.", true,
                new { nuspec.RepositoryUrl, nuspec.RepositoryType, nuspec.Commit }));
            var sbom = options.TryGetValue("--sbom", out var sbomPath) ? ReadSbom(sbomPath) : null;
            var dependencyPackages = sbom?.Packages.Where(p => !p.IsRoot).ToArray() ?? [];
            bool dependencyViolation = dependencyPackages.Any(p => p.Policy.State is "disallowed" or "malformed");
            bool dependencyUnresolved = dependencyPackages.Any(p => p.Policy.State == "unresolved");
            string dependencyStatus = dependencyPackages.Length == 0 || dependencyUnresolved ? Untested :
                dependencyViolation ? "gap" : dependencyPackages.All(p => p.Policy.Approved) ? "verified" : Untested;
            if (dependencyViolation) dependencyStatus = "gap";
            rows.Add(sbom is null
                ? NotTested("LP-03", "E", "No SBOM supplied; dependency license policy was not tested.")
                : new("LP-03", dependencyStatus, "E",
                    dependencyViolation ? "resolved-dependency-license-policy-violation" :
                        dependencyUnresolved ? "dependency-license-evidence-unresolved" :
                        dependencyPackages.Length == 0 ? "no-in-scope-sbom-dependencies" :
                        "declared-dependency-license-policy-only",
                    dependencyViolation
                        ? "At least one resolved in-scope dependency has an explicitly disallowed copyleft license."
                        : "License policy covers only resolved SBOM declarations, not dependency completeness.", true,
                    new { sbom.Format, sbom.NpmCount, sbom.NugetCount, sbom.Packages, completenessProven = false,
                        inScopeDependencyCount = dependencyPackages.Length, dependencyViolation, dependencyUnresolved,
                        caveat = "An SBOM does not prove that all bundled, runtime, transitive, or build dependencies were declared." }));
            var documents = ReadLicenseDocuments(package);
            var web = InventoryWebAssets(package);
            var runtimeWebAssets = web.Where(a => a.Extension is ".js" or ".mjs" or ".cjs" or ".css" or ".wasm" or ".map")
                .ToArray();
            var packagedLicenseSignals = documents.SelectMany(d => d.LicenseSignals)
                .Concat(web.SelectMany(a => a.LicenseSignals)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            bool obviousCopyleft = packagedLicenseSignals.Any(LicensePolicy.IsCopyleftSignal);
            rows.Add(Field("LP-05", !fileLicense && nuspec.LicenseType == "expression" && !string.IsNullOrWhiteSpace(nuspec.License),
                "license-expression", new { nuspec.License, nuspec.LicenseType }));
            rows.Add(Field("LP-06", IsHttpUrl(nuspec.RepositoryUrl), "repository-url", new { nuspec.RepositoryUrl, nuspec.RepositoryType }));
            rows.Add(Field("LP-07", Regex.IsMatch(nuspec.Commit, @"\A(?:[a-fA-F0-9]{40}|[a-fA-F0-9]{64})\z"),
                "full-repository-commit", new { nuspec.Commit }));
            rows.Add(Field("LP-08", !string.IsNullOrWhiteSpace(nuspec.Authors), "authors", new { nuspec.Authors }));
            rows.Add(Field("LP-09", IsHttpUrl(nuspec.ProjectUrl), "project-url", new { nuspec.ProjectUrl }));
            var pe = InspectImages(package, options.GetValueOrDefault("--expected-publisher"));
            var managed = pe.Where(p => p.Managed).ToArray();
            bool malformedPe = pe.Any(p => p.Error is not null);
            var resourceAssets = pe.SelectMany(p => p.Resources).ToArray();
            rows.Insert(3, new("LP-04", obviousCopyleft ? "gap" : Untested, "E",
                obviousCopyleft ? "packaged-copyleft-needs-review" : "attribution-completeness-unproven",
                obviousCopyleft ? "Packaged copyleft notices require review; this is not an infringement finding." :
                    "Notices and banners are evidence, not proof of attribution completeness.", true,
                new { documents, packagedLicenseSignals, webAssetCount = web.Count,
                    manifestResourceCount = resourceAssets.Length }));
            rows.Add(new("PI-01", malformedPe ? "gap" : managed.Any(p => p.StrongName?.Status == "gap") ? "gap" :
                    managed.Length == 0 ? "not applicable" :
                    managed.All(p => p.StrongName?.Status == "verified") ? "verified" : "gap", "D",
                malformedPe ? "malformed-pe" : managed.Length == 0 ? "no-managed-images" :
                    managed.All(p => p.StrongName?.Status == "verified") ? "strong-name-signatures-valid" : "strong-name-gap",
                "Strong names are cryptographic integrity evidence, not publisher trust or a security boundary.", malformedPe,
                new { images = pe.Select(p => new { p.Path, p.Managed, p.InformationalVersion, p.StrongName, p.Error }) }));
            rows.Add(new("PI-02", malformedPe ? "gap" : pe.Any(p => p.Authenticode?.Status == "gap") ? "gap" :
                    pe.Count == 0 ? "not applicable" :
                    pe.All(p => p.Authenticode?.Status == "verified") ? "verified" : Untested, "D",
                malformedPe ? "malformed-pe" : "authenticode-integrity-and-trust-separated",
                "Offline CMS and PE-digest integrity do not establish publisher trust; nested signatures are not examined.", true,
                new { expectedPublisher = options.GetValueOrDefault("--expected-publisher"),
                    images = pe.Select(p => new { p.Path, p.Authenticode, p.Error }), nestedSignaturesExamined = false }));
            var packageSignature = await InspectPackageSignatureAsync(package);
            rows.Add(new("PI-03", packageSignature.Valid ? "verified" : "gap", "D",
                packageSignature.Valid ? "nuget-signature-integrity-valid" : "nuget-signature-integrity-gap",
                "NuGet CMS signature and package integrity only; certificate trust requires separate dotnet nuget verify.", true, packageSignature));
            rows.Add(new("PI-04", packageSignature.HasRepositorySignature ? Untested : "gap", "X",
                packageSignature.HasRepositorySignature ? "repository-signature-owner-validation-required" : "no-repository-signature",
                "Repository primary/countersignature presence does not identify NuGet.org or establish repository trust.", true,
                new { packageSignature.SignerType, packageSignature.RepositorySignature, packageSignature.HasRepositorySignature,
                    trust = "Run dotnet nuget verify --all against the intended trust policy separately." }));
            rows.Add(new("PI-05", Untested, "E", "trusted-publishing-evidence-needed",
                "OIDC or trusted-publishing configuration cannot be established from package bytes.", true,
                new { packageSignature.SignerType, packageSignature.HasRepositorySignature }));
            rows.Add(new("PI-06", Untested, "E", "sbom-publication-per-release-unproven",
                "A supplied local SBOM does not prove that an SBOM is published for every release.", true,
                new { sbomProvided = sbom is not null, format = sbom?.Format }));
            rows.Add(new("PI-07", Untested, "E", "dependency-graph-reconciliation-not-performed",
                "Direct and transitive SBOM completeness requires reconciliation with restored dependency graphs.", true,
                new { sbomProvided = sbom is not null, sbom?.NugetCount, sbom?.NpmCount,
                    packageCount = sbom?.Packages.Count ?? 0, lockProvided = options.ContainsKey("--package-lock") }));
            var lockInventory = options.TryGetValue("--package-lock", out var lockPath) ? ReadLock(lockPath) : null;
            bool noWeb = web.Count == 0 && resourceAssets.Length == 0 && !malformedPe;
            var coverage = ResolveCoverage(web, resourceAssets, sbom);
            rows.Add(new("PI-08", noWeb ? "not applicable" : Untested, "E",
                noWeb ? "no-web-assets" : "bundled-web-components-unresolved",
                "Bundled JavaScript/CSS component names and versions require SBOM/upstream adjudication; hashes alone do not verify this row.", !noWeb,
                new { webAssetCount = web.Count, manifestResourceCount = resourceAssets.Length, coverage, lockInventory }));
            rows.Add(new("PI-09", noWeb ? "not applicable" : obviousCopyleft ? "gap" : Untested, "E",
                noWeb ? "no-web-assets" : obviousCopyleft ? "packaged-copyleft-needs-review" : "third-party-notice-preservation-unproven",
                "Required bundled third-party notices need adjudication; hash coverage alone never verifies preservation.", !noWeb,
                new { documents, packagedLicenseSignals, coverage, webAssetCount = web.Count,
                    manifestResourceCount = resourceAssets.Length }));

            Parity? parity = null;
            Archive? release = null;
            if (options.TryGetValue("--release-nupkg", out var releasePath))
            {
                release = ReadArchive(releasePath);
                parity = CompareParity(package, release);
            }
            int pi06Index = rows.FindIndex(r => r.Id == "PI-06");
            rows[pi06Index] = new("PI-06", Untested, "E", "sbom-publication-per-release-unproven",
                "A supplied local SBOM does not prove that an SBOM is published for every release.", true,
                new { sbomProvided = sbom is not null, format = sbom?.Format, releaseParity = parity });
            object? offlineAttestation = options.TryGetValue("--attestation", out var attestationPath)
                ? ReadOfflineAttestation(attestationPath) : null;
            var finalProvenance = options.TryGetValue("--attestation-repo", out var repository)
                ? await VerifyAttestationAsync(package, repository, false)
                : new Provenance(false, false, "gh-verification-not-requested", null);
            var releaseProvenance = options.TryGetValue("--attestation-repo", out repository) &&
                release is not null && parity?.Matches == true
                    ? await VerifyAttestationAsync(release, repository, true)
                    : new Provenance(false, false, "parity-release-verification-not-available", null);
            var buildProvenance = finalProvenance.BuildVerified ? finalProvenance :
                releaseProvenance.BuildVerified ? releaseProvenance : finalProvenance;
            bool sbomBindsFinalPackage = sbom?.BindingSha256Digests.Contains(package.Sha256, StringComparer.OrdinalIgnoreCase) == true;
            bool finalDigestBound = finalProvenance.SourceVerified && sbomBindsFinalPackage;
            rows.Add(new("PI-10", finalDigestBound ? "verified" : Untested, "X",
                finalDigestBound ? "final-package-sbom-and-provenance-bound" :
                    finalProvenance.SourceVerified ? "sbom-missing-final-package-sha256" : finalProvenance.ReasonCode,
                "The exact final signed package digest must be bound by both SBOM and gh-verified provenance.", true,
                new { provenance = finalProvenance, sbomBindsFinalPackage, finalPackageSha256 = package.Sha256,
                    sbomObservedSha256Digests = sbom?.ObservedSha256Digests,
                    sbomBindingSha256Digests = sbom?.BindingSha256Digests,
                    offlineAttestation, releaseParity = parity }));
            rows.Add(new("PI-11", buildProvenance.BuildVerified ? "verified" : Untested, "X", buildProvenance.ReasonCode,
                "Build provenance additionally needs recognized SLSA build, workflow, builder and invocation semantics.", true,
                new { provenance = buildProvenance, offlineAttestation, releaseParity = parity,
                    packageSignature, finalPackageProvenance = finalProvenance, parityReleaseProvenance = releaseProvenance,
                    verifiedDigest = ReferenceEquals(buildProvenance, releaseProvenance) ? release?.Sha256 : package.Sha256,
                    selectedReleasePackage = ReferenceEquals(buildProvenance, releaseProvenance) }));
            rows.Add(new("PI-12", "owner evidence required", "X", "owner-process-evidence-required",
                "The owner must provide and maintain a policy for retaining release evidence and verification records.", true, new { }));
            rows.Insert(9, new("LP-10", Untested, "E", "packaged-binary-source-attribution-unproven",
                "Every packaged binary must map to public source or a clearly referenced upstream package and release.", true,
                new
                {
                    nuspec.RepositoryUrl, nuspec.Commit,
                    assemblies = managed.Select(p => new { p.Path, p.InformationalVersion }),
                    provenance = buildProvenance, releaseParity = parity, webAssetCount = web.Count
                }));
            var deferred = DeferredRows();
            var report = new
            {
                schemaVersion = "1.0", scope = "lp-pi-22-of-60",
                package = new { path = package.Path, sha256 = package.Sha256, nuspec.Id, nuspec.Version,
                    entryCount = package.AllEntries.Count, fileCount = package.Entries.Count,
                    totalBytes = package.Entries.Sum(e => (long)e.Bytes.Length) },
                limitations = new[] { "Static evidence only; no assemblies loaded and no entries extracted.",
                    "Not a full 60-row readiness assessment. Trust, completeness and legal conclusions are separate.",
                    "No YAML or JavaScript AST parsing. Nested Authenticode signatures are not examined." },
                rows, deferredRows = deferred,
                inventory = new
                {
                    entries = package.AllEntries,
                    webAssets = web, webAssetsByExtension = web.GroupBy(a => a.Extension).OrderBy(g => g.Key, StringComparer.Ordinal)
                        .Select(g => new { extension = g.Key, count = g.Count(), totalBytes = g.Sum(a => (long)a.TotalBytes),
                            sha256 = AggregateHash(g.Select(a => (a.Path, a.Sha256))) }),
                    runtimeWebAssetSummary = new
                    {
                        count = runtimeWebAssets.Length,
                        totalBytes = runtimeWebAssets.Sum(a => (long)a.TotalBytes),
                        sha256 = AggregateHash(runtimeWebAssets.Select(a => (a.Path, a.Sha256))),
                        byExtension = runtimeWebAssets.GroupBy(a => a.Extension).OrderBy(g => g.Key, StringComparer.Ordinal)
                            .Select(g => new { extension = g.Key, count = g.Count(),
                                totalBytes = g.Sum(a => (long)a.TotalBytes),
                                sha256 = AggregateHash(g.Select(a => (a.Path, a.Sha256))) })
                    },
                    manifestResources = resourceAssets,
                    manifestResourcesByExtension = resourceAssets.GroupBy(a => Path.GetExtension(a.Path).ToLowerInvariant())
                        .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new { extension = g.Key, count = g.Count(),
                            totalBytes = g.Sum(a => (long)a.TotalBytes), sha256 = AggregateHash(g.Select(a => (a.Path, a.Sha256))) }),
                    licenseDocuments = documents, releaseParity = parity
                }
            };
            string json = JsonSerializer.Serialize(report, JsonOptions);
            if (options.TryGetValue("--output", out var output))
            {
                if (options.Where(kv => kv.Key is not "--output" and not "--expected-publisher" and not "--attestation-repo")
                    .Any(kv => Path.GetFullPath(kv.Value).Equals(Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("--output must not overwrite an input.");
                await File.WriteAllTextAsync(output, json + Environment.NewLine, new UTF8Encoding(false));
            }
            else Console.WriteLine(json);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
            XmlException or JsonException or CryptographicException or NotSupportedException or OverflowException or
            InvalidOperationException or BadImageFormatException)
        {
            Console.Error.WriteLine($"INVALID_INPUT: {ex.Message}");
            return 2;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        string[] names = ["--nupkg", "--release-nupkg", "--sbom", "--attestation", "--attestation-repo",
            "--package-lock", "--expected-publisher", "--output"];
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            if (!names.Contains(args[i], StringComparer.Ordinal) || i + 1 == args.Length ||
                args[i + 1].StartsWith("--", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(args[i + 1]))
                throw new ArgumentException($"Expected an option and value, got '{args[i]}'. Required: --nupkg <file>.");
            string key = args[i++];
            if (!result.TryAdd(key, args[i])) throw new ArgumentException($"Duplicate option: {key}");
        }
        if (!result.ContainsKey("--nupkg")) throw new ArgumentException("--nupkg <file> is required.");
        if (result.TryGetValue("--attestation-repo", out var repo) &&
            !Regex.IsMatch(repo, @"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z"))
            throw new ArgumentException("--attestation-repo must be owner/repository.");
        return result;
    }

    private static Row NotTested(string id, string basis, string summary) =>
        new(id, Untested, basis, "external-evidence-needed", summary, true, new { });
    private static Row Field(string id, bool present, string field, object facts) =>
        new(id, present ? "verified" : "gap", "D", present ? $"{field}-present" : $"{field}-missing-or-invalid",
            $"Checks {field} metadata presence/shape only, not its accuracy.", !present, facts);
    private static bool IsHttpUrl(string text) => Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
        uri.Scheme is "https" or "http" && !string.IsNullOrWhiteSpace(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);
    private static DeferredRow[] DeferredRows()
    {
        string[] ids = [.. Enumerable.Range(1, 9).Select(i => $"SEC-{i:00}"), "BEQ-21", "BEQ-24", "TA-07", "CI-01",
            .. Enumerable.Range(5, 4).Select(i => $"CI-{i:00}"), .. Enumerable.Range(1, 8).Select(i => $"SUP-{i:00}"),
            "SUP-10", .. Enumerable.Range(1, 6).Select(i => $"SCF-{i:00}"), .. Enumerable.Range(1, 6).Select(i => $"AI-{i:00}")];
        return ids.Select(id => new DeferredRow(id, Untested, "outside-lp-pi-scope",
            "Deferred: outside this 22-row package inspection.", false, new { }, true)).ToArray();
    }

    private static byte[] ReadBounded(Stream stream, long limit)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        long count = 0;
        int read;
        while ((read = stream.Read(chunk)) != 0)
        {
            count = checked(count + read);
            if (count > limit) throw new InvalidDataException($"Stream exceeds {limit} bytes.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
    private static byte[] ReadFile(string path, long limit)
    {
        using var stream = File.OpenRead(path);
        return ReadBounded(stream, limit);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string AggregateHash(IEnumerable<(string Path, string Hash)> entries) =>
        Hash(Encoding.UTF8.GetBytes(string.Concat(entries.OrderBy(e => e.Path, StringComparer.Ordinal)
            .Select(e => $"{e.Path}\0{e.Hash}\n"))));
    private static ushort U16(byte[] b, int p) => BinaryPrimitives.ReadUInt16LittleEndian(Slice(b, p, 2));
    private static uint U32(byte[] b, int p) => BinaryPrimitives.ReadUInt32LittleEndian(Slice(b, p, 4));
    private static ReadOnlySpan<byte> Slice(byte[] b, int offset, int count)
    {
        if (offset < 0 || count < 0 || (long)offset + count > b.Length) throw new InvalidDataException("Out-of-range binary structure.");
        return b.AsSpan(offset, count);
    }
    private static int Int(uint value) => checked((int)value);

    private static string NormalizeEntry(string name)
    {
        string path = name.Replace('\\', '/').Normalize(NormalizationForm.FormC);
        if (path.Length == 0 || path.StartsWith('/') || path.Contains(':') || path.Any(c => char.IsControl(c)))
            throw new InvalidDataException($"Unsafe ZIP path: {name}");
        string[] parts = path.TrimEnd('/').Split('/');
        if (parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ')))
            throw new InvalidDataException($"Unsafe ZIP path: {name}");
        return string.Join("/", parts);
    }

    private static Archive ReadArchive(string path)
    {
        byte[] bytes = ReadFile(path, MaxArchiveBytes);
        int end = -1;
        for (int p = bytes.Length - 22; p >= Math.Max(0, bytes.Length - 65557); p--)
        {
            if (U32(bytes, p) == 0x06054b50 && p + 22L + U16(bytes, p + 20) == bytes.Length) { end = p; break; }
        }
        if (end < 0) throw new InvalidDataException("ZIP end-of-central-directory is missing.");
        int count = U16(bytes, end + 10);
        if (U16(bytes, end + 4) != 0 || U16(bytes, end + 6) != 0 || U16(bytes, end + 8) != count ||
            count > MaxEntries || count == ushort.MaxValue)
            throw new InvalidDataException("Multi-disk, ZIP64 or excessive ZIP entry count is not supported.");
        int cdSize = Int(U32(bytes, end + 12)), cdStart = Int(U32(bytes, end + 16));
        if ((long)cdStart + cdSize != end) throw new InvalidDataException("Invalid ZIP central-directory bounds.");
        int cursor = cdStart;
        var central = new List<CentralEntry>();
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long declaredTotal = 0;
        for (int i = 0; i < count; i++)
        {
            if (U32(bytes, cursor) != 0x02014b50) throw new InvalidDataException("Invalid ZIP central-directory record.");
            ushort flags = U16(bytes, cursor + 8), method = U16(bytes, cursor + 10);
            if ((flags & ~0x080e) != 0 || method is not 0 and not 8 || U16(bytes, cursor + 34) != 0)
                throw new InvalidDataException("Encrypted, multi-disk or unsupported ZIP entry.");
            int compressed = Int(U32(bytes, cursor + 20)), expanded = Int(U32(bytes, cursor + 24));
            int nameSize = U16(bytes, cursor + 28), extraSize = U16(bytes, cursor + 30), commentSize = U16(bytes, cursor + 32);
            var nameBytes = Slice(bytes, cursor + 46, nameSize).ToArray();
            if ((flags & 0x800) == 0 && nameBytes.Any(b => b >= 128))
                throw new InvalidDataException("Non-ASCII ZIP names must declare UTF-8.");
            string rawName = new UTF8Encoding(false, true).GetString(nameBytes);
            string normalized = NormalizeEntry(rawName);
            if (!aliases.Add(normalized)) throw new InvalidDataException($"Duplicate normalized ZIP alias: {rawName}");
            if (expanded > MaxEntryBytes || (declaredTotal += expanded) > MaxTotalBytes)
                throw new InvalidDataException("ZIP declared expansion exceeds limits.");
            int local = Int(U32(bytes, cursor + 42));
            if (U32(bytes, local) != 0x04034b50 || U16(bytes, local + 6) != flags || U16(bytes, local + 8) != method ||
                U16(bytes, local + 26) != nameSize ||
                !Slice(bytes, local + 30, nameSize).SequenceEqual(nameBytes))
                throw new InvalidDataException("ZIP local and central records disagree.");
            int data = checked(local + 30 + nameSize + U16(bytes, local + 28));
            int tail = checked(data + compressed);
            uint crc = U32(bytes, cursor + 16);
            if ((flags & 8) == 0)
            {
                if (U32(bytes, local + 14) != crc || U32(bytes, local + 18) != compressed || U32(bytes, local + 22) != expanded)
                    throw new InvalidDataException("ZIP local sizes/CRC disagree.");
            }
            else
            {
                int descriptor = tail;
                if (U32(bytes, descriptor) == 0x08074b50) descriptor += 4;
                if (U32(bytes, descriptor) != crc || U32(bytes, descriptor + 4) != compressed || U32(bytes, descriptor + 8) != expanded)
                    throw new InvalidDataException("Invalid ZIP data descriptor.");
                tail = checked(descriptor + 12);
            }
            if (local < 0 || tail > cdStart || data > cdStart)
                throw new InvalidDataException("ZIP entry intersects central directory.");
            central.Add(new(rawName, normalized, local, tail, expanded, crc, rawName.EndsWith('/') || rawName.EndsWith('\\')));
            cursor = checked(cursor + 46 + nameSize + extraSize + commentSize);
            if (cursor > end) throw new InvalidDataException("ZIP central-directory record exceeds bounds.");
        }
        if (cursor != end) throw new InvalidDataException("ZIP central-directory size/count mismatch.");
        int next = 0;
        foreach (var e in central.OrderBy(e => e.Local))
        {
            if (e.Local != next) throw new InvalidDataException("Overlapping, orphaned or prefixed ZIP local records.");
            next = e.End;
        }
        if (next != cdStart) throw new InvalidDataException("Unaccounted bytes before ZIP central directory.");
        var entries = new List<Entry>();
        var summaries = new List<EntrySummary>();
        using var archive = new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read);
        if (archive.Entries.Count != central.Count) throw new InvalidDataException("ZIP parser entry-count disagreement.");
        long actualTotal = 0;
        for (int i = 0; i < central.Count; i++)
        {
            var item = central[i];
            var zipEntry = archive.Entries[i];
            if (zipEntry.FullName != item.RawName) throw new InvalidDataException("ZIP parser name disagreement.");
            using var stream = zipEntry.Open();
            byte[] content = ReadBounded(stream, Math.Min(MaxEntryBytes, MaxTotalBytes - actualTotal));
            actualTotal = checked(actualTotal + content.LongLength);
            if (content.Length != item.Expanded || Crc32(content) != item.Crc || item.Directory && content.Length != 0)
                throw new InvalidDataException($"ZIP streamed size/CRC mismatch: {item.RawName}");
            string sha256 = Hash(content);
            summaries.Add(new(item.Name + (item.Directory ? "/" : ""), content.Length, sha256, item.Directory));
            if (!item.Directory) entries.Add(new(item.Name, content, sha256));
        }
        return new(Path.GetFullPath(path), bytes, Hash(bytes), entries, summaries);
    }

    private static uint Crc32(byte[] bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes) crc = CrcTable.Values[(crc ^ value) & 255] ^ (crc >> 8);
        return ~crc;
    }
    private static Entry? FindEntry(Archive archive, string name) =>
        archive.Entries.FirstOrDefault(e => e.Name.Equals(name.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
    private static Nuspec ReadNuspec(Archive archive)
    {
        var specs = archive.Entries.Where(e => !e.Name.Contains('/') && e.Name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (specs.Length != 1) throw new InvalidDataException("Expected exactly one root .nuspec.");
        using var stream = new MemoryStream(specs[0].Bytes, false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxEntryBytes,
            MaxCharactersFromEntities = 0
        });
        var doc = XDocument.Load(reader);
        var metadata = doc.Root?.Elements().SingleOrDefault(e => e.Name.LocalName == "metadata")
            ?? throw new InvalidDataException("Nuspec metadata missing.");
        string Value(string key) => metadata.Elements().SingleOrDefault(e => e.Name.LocalName == key)?.Value.Trim() ?? "";
        var license = metadata.Elements().SingleOrDefault(e => e.Name.LocalName == "license");
        var repository = metadata.Elements().SingleOrDefault(e => e.Name.LocalName == "repository");
        return new(Value("id"), Value("version"), Value("authors"), Value("projectUrl"), license?.Value.Trim() ?? "",
            license?.Attribute("type")?.Value.Trim() ?? "", repository?.Attribute("url")?.Value.Trim() ?? "",
            repository?.Attribute("type")?.Value.Trim() ?? "", repository?.Attribute("commit")?.Value.Trim() ?? "");
    }

    private static bool ResourceWebName(string name) =>
        new[] { ".js", ".mjs", ".cjs", ".css", ".wasm" }.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    private static bool WebEntry(Entry entry)
    {
        string path = entry.Name.ToLowerInvariant();
        string[] segments = path.Split('/');
        bool root = segments.Any(s => s is "staticwebassets" or "wwwroot" or "content" or "contentfiles");
        string extension = Path.GetExtension(path);
        bool webExtension = extension is ".js" or ".mjs" or ".cjs" or ".css" or ".wasm";
        bool buildStaticManifest = segments.Any(s => s.StartsWith("build", StringComparison.Ordinal)) &&
            extension is ".props" or ".targets" or ".json" &&
            (path.Contains("staticwebassets", StringComparison.Ordinal) ||
                path.Contains("staticwebasset", StringComparison.Ordinal) ||
                path.Contains("wwwroot", StringComparison.Ordinal));
        return root || webExtension || buildStaticManifest;
    }
    private static List<WebAsset> InventoryWebAssets(Archive package) => package.Entries.Where(WebEntry)
        .OrderBy(e => e.Name, StringComparer.Ordinal).Select(e =>
        {
            string text = IsText(e.Name) ? DecodeText(e.Bytes) : "";
            string[] maps = Regex.Matches(text, @"[#@]\s*sourceMappingURL\s*=\s*([^\s*]+)", RegexOptions.CultureInvariant)
                .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).Take(256).ToArray();
            string[] sources = [];
            if (e.Name.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var map = ParseJson(e.Bytes);
                    sources = Array(map.RootElement, "sources").Where(s => s.ValueKind == JsonValueKind.String)
                        .Select(s => s.GetString()!).Take(4096).ToArray();
                }
                catch (JsonException) { sources = ["[invalid source-map JSON]"]; }
            }
            return new WebAsset(e.Name, Path.GetExtension(e.Name).ToLowerInvariant(), e.Bytes.Length, e.Sha256,
                LicenseSignals(text), maps, sources);
        }).ToList();
    private static bool IsText(string name) => Path.GetExtension(name).ToLowerInvariant() is not ".wasm" and not ".woff" and not ".woff2";
    private static string DecodeText(byte[] bytes) => Encoding.UTF8.GetString(bytes);
    private static string[] LicenseSignals(string text)
    {
        var signals = new List<string>();
        void Capture(ReadOnlySpan<char> value)
        {
            string bounded = value[..Math.Min(value.Length, 16384)].ToString();
            signals.AddRange(bounded.Split(['|', ';']).Select(v => v.Trim()).Where(v => v.Length > 0).Take(128)
                .Select(v => v.Length > 4096 ? v[..4096] : v));
        }
        // Linear scans avoid quadratic matching on large, minified bundles.
        int cursor = 0;
        while (signals.Count < 512)
        {
            int start = text.IndexOf("/*", cursor, StringComparison.Ordinal);
            if (start < 0) break;
            int end = text.IndexOf("*/", start + 2, StringComparison.Ordinal);
            if (end < 0) break;
            var comment = text.AsSpan(start, end + 2 - start);
            if (comment.StartsWith("/*!") || comment.Contains("@license", StringComparison.OrdinalIgnoreCase)) Capture(comment);
            cursor = end + 2;
        }
        cursor = 0;
        while (signals.Count < 512)
        {
            int start = text.IndexOf("SPDX-License-Identifier:", cursor, StringComparison.OrdinalIgnoreCase);
            if (start < 0) break;
            int end = text.IndexOfAny(['\r', '\n'], start);
            if (end < 0) end = text.Length;
            Capture(text.AsSpan(start, end - start));
            cursor = end;
        }
        cursor = 0;
        while (signals.Count < 512)
        {
            int start = text.IndexOf("@license", cursor, StringComparison.OrdinalIgnoreCase);
            if (start < 0) break;
            int end = text.IndexOfAny(['\r', '\n'], start);
            if (end < 0) end = text.Length;
            Capture(text.AsSpan(start, end - start));
            cursor = end;
        }
        if (Regex.IsMatch(text, @"GNU (?:AFFERO |LESSER |LIBRARY )?GENERAL PUBLIC LICENSE", RegexOptions.IgnoreCase))
            signals.Add("GNU GENERAL PUBLIC LICENSE family text");
        return signals.Distinct(StringComparer.Ordinal).Take(512).ToArray();
    }
    private static List<LicenseDocument> ReadLicenseDocuments(Archive package) => package.Entries
        .Where(e => Regex.IsMatch(Path.GetFileName(e.Name), @"(?:LICENSE|LICENCE|NOTICE|THIRD[-_. ]?PARTY)", RegexOptions.IgnoreCase))
        .OrderBy(e => e.Name, StringComparer.Ordinal).Select(e =>
        {
            string text = DecodeText(e.Bytes);
            string[] lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
            return new LicenseDocument(e.Name, e.Sha256,
                lines.Where(l => l.StartsWith('#') || l.EndsWith(':') || Regex.IsMatch(l, @"^[A-Z][A-Z0-9 \-]{4,}$"))
                    .Take(128).Select(l => l[..Math.Min(l.Length, 1024)]).ToArray(),
                lines.Where(l => l.Contains("copyright", StringComparison.OrdinalIgnoreCase) || l.Contains('©'))
                    .Take(256).Select(l => l[..Math.Min(l.Length, 2048)]).ToArray(), LicenseSignals(text));
        }).ToList();

    private static List<PeEvidence> InspectImages(Archive package, string? publisher)
    {
        var result = new List<PeEvidence>();
        foreach (var entry in package.Entries.Where(e => e.Bytes.Length >= 2 && e.Bytes[0] == 'M' && e.Bytes[1] == 'Z'))
        {
            var evidence = new PeEvidence(entry.Name);
            result.Add(evidence);
            try
            {
                using var pe = new PEReader(new MemoryStream(entry.Bytes, false));
                var layout = PeLayout.Read(entry.Bytes);
                evidence.Authenticode = InspectAuthenticode(entry.Bytes, layout, publisher);
                evidence.Managed = pe.PEHeaders.CorHeader is not null;
                if (!evidence.Managed) continue;
                if (!pe.HasMetadata) throw new BadImageFormatException("CLI image has no readable metadata.");
                var metadata = pe.GetMetadataReader();
                if (!metadata.IsAssembly) throw new BadImageFormatException("CLI module has no assembly identity.");
                var assembly = metadata.GetAssemblyDefinition();
                evidence.StrongName = InspectStrongName(entry.Bytes, pe.PEHeaders, layout, metadata, assembly);
                evidence.InformationalVersion = InformationalVersion(metadata, assembly);
                foreach (var handle in metadata.ManifestResources)
                {
                    var resource = metadata.GetManifestResource(handle);
                    string name = metadata.GetString(resource.Name);
                    if (!ResourceWebName(name)) continue;
                    if (!resource.Implementation.IsNil)
                    {
                        evidence.Resources.Add(new(entry.Name + "::" + name, 0, "", "linked-resource-not-contained"));
                        continue;
                    }
                    var directory = pe.PEHeaders.CorHeader!.ResourcesDirectory;
                    int start = layout.RvaToOffset(directory.RelativeVirtualAddress, directory.Size);
                    if (resource.Offset > int.MaxValue) throw new BadImageFormatException("Resource offset exceeds bounds.");
                    int offset = checked(start + (int)resource.Offset);
                    int size = Int(U32(entry.Bytes, offset));
                    if ((long)resource.Offset + 4 + size > directory.Size)
                        throw new BadImageFormatException("ManifestResource exceeds CLR resource directory.");
                    evidence.Resources.Add(new(entry.Name + "::" + name, size,
                        Hash(Slice(entry.Bytes, offset + 4, size).ToArray()), null));
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException or IOException or InvalidDataException or ArgumentException or
                InvalidOperationException or OverflowException or CryptographicException)
            {
                evidence.Error = $"{ex.GetType().Name}: {ex.Message}";
            }
        }
        return result;
    }

    private static string? InformationalVersion(MetadataReader reader, AssemblyDefinition assembly)
    {
        foreach (var handle in assembly.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            EntityHandle type = attribute.Constructor.Kind switch
            {
                HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
                HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
                _ => default
            };
            string? ns = null, name = null;
            if (type.Kind == HandleKind.TypeReference)
            {
                var reference = reader.GetTypeReference((TypeReferenceHandle)type);
                ns = reader.GetString(reference.Namespace); name = reader.GetString(reference.Name);
            }
            else if (type.Kind == HandleKind.TypeDefinition)
            {
                var definition = reader.GetTypeDefinition((TypeDefinitionHandle)type);
                ns = reader.GetString(definition.Namespace); name = reader.GetString(definition.Name);
            }
            if (ns != "System.Reflection" || name != "AssemblyInformationalVersionAttribute") continue;
            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) throw new BadImageFormatException("Invalid informational-version custom attribute.");
            return blob.ReadSerializedString();
        }
        return null;
    }

    private static Check InspectStrongName(byte[] image, PEHeaders headers, PeLayout layout, MetadataReader metadata, AssemblyDefinition assembly)
    {
        var cor = headers.CorHeader!;
        bool signed = (cor.Flags & CorFlags.StrongNameSigned) != 0;
        byte[] key = metadata.GetBlobBytes(assembly.PublicKey);
        var directory = cor.StrongNameSignatureDirectory;
        var basic = new { strongNameSigned = signed, publicKeyBytes = key.Length, signatureDirectoryRva = directory.RelativeVirtualAddress,
            signatureDirectoryBytes = directory.Size, assemblyHashAlgorithm = assembly.HashAlgorithm.ToString() };
        if (key.Length == 0) return new("gap", signed ? "no-public-key" : "unsigned", basic);
        if (directory.Size == 0) return new("gap", signed ? "signature-directory-missing" : "delay-signed", basic);
        int offset = layout.RvaToOffset(directory.RelativeVirtualAddress, directory.Size);
        byte[] signature = Slice(image, offset, directory.Size).ToArray();
        if (signature.All(b => b == 0)) return new("gap", "delay-signed", basic);
        if (!signed) return new("gap", "strong-name-signed-flag-not-set", basic);
        try
        {
            if (key.Length < 32 || U32(key, 8) != key.Length - 12 || key[12] != 6 || key[13] != 2 ||
                U16(key, 14) != 0 || U32(key, 16) != 0x2400 || U32(key, 20) != 0x31415352)
                return new("gap", "unsupported-strong-name-public-key", basic);
            int bits = Int(U32(key, 24));
            uint exponentValue = U32(key, 28);
            if (bits < 512 || bits > 16384 || bits % 8 != 0 || key.Length != 32 + bits / 8 || signature.Length != bits / 8 || exponentValue < 3)
                return new("gap", "invalid-strong-name-key-or-signature-size", basic);
            byte[] modulus = key.AsSpan(32).ToArray();
            System.Array.Reverse(modulus);
            byte[] exponent = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(exponent, exponentValue);
            exponent = exponent.SkipWhile(b => b == 0).ToArray();
            uint algorithm = U32(key, 4);
            if (algorithm == 0) algorithm = (uint)assembly.HashAlgorithm;
            if (algorithm == 0) algorithm = 0x8004;
            HashAlgorithmName hashName = algorithm switch { 0x8004 => HashAlgorithmName.SHA1, 0x800c => HashAlgorithmName.SHA256, _ => default };
            if (hashName == default) return new("gap", "unsupported-strong-name-hash", basic);
            byte[] header = Slice(image, 0, layout.HeaderEnd).ToArray();
            System.Array.Clear(header, layout.ChecksumOffset, 4);
            if (layout.SecurityDirectoryOffset >= 0) System.Array.Clear(header, layout.SecurityDirectoryOffset, 8);
            using var hash = IncrementalHash.CreateHash(hashName);
            hash.AppendData(header);
            bool signatureCovered = false;
            foreach (var section in layout.Sections)
            {
                if (offset >= section.Offset && (long)offset + signature.Length <= (long)section.Offset + section.Size)
                {
                    hash.AppendData(Slice(image, section.Offset, offset - section.Offset));
                    int after = checked(offset + signature.Length);
                    hash.AppendData(Slice(image, after, section.Offset + section.Size - after));
                    signatureCovered = true;
                }
                else hash.AppendData(Slice(image, section.Offset, section.Size));
            }
            if (!signatureCovered) return new("gap", "signature-not-in-raw-section", basic);
            System.Array.Reverse(signature); // CAPI/ECMA-335 stores the RSA signature little-endian.
            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters { Modulus = modulus, Exponent = exponent });
            bool valid = rsa.VerifyHash(hash.GetHashAndReset(), signature, hashName, RSASignaturePadding.Pkcs1);
            return new(valid ? "verified" : "gap", valid ? "rsa-strong-name-valid" : "invalid-strong-name-signature",
                new { evidence = basic, hashAlgorithm = hashName.Name, keyBits = bits, publisherTrustEstablished = false });
        }
        catch (CryptographicException ex) { return new("gap", "invalid-strong-name-cryptography", new { evidence = basic, error = ex.Message }); }
    }

    private static Check InspectAuthenticode(byte[] image, PeLayout layout, string? expectedPublisher)
    {
        if (layout.CertificateSize == 0) return new("gap", "authenticode-missing", new { nestedSignaturesExamined = false });
        var signatures = new List<object>();
        bool allValid = true, allTrusted = true;
        int position = layout.CertificateOffset, end = checked(position + layout.CertificateSize);
        try
        {
            while (position < end)
            {
                int length = Int(U32(image, position));
                if (length < 8 || (long)position + length > end || U16(image, position + 4) != 0x200 || U16(image, position + 6) != 2)
                    throw new InvalidDataException("Invalid WIN_CERTIFICATE PKCS#7 record.");
                var cms = new SignedCms();
                cms.Decode(Slice(image, position + 8, length - 8).ToArray());
                if (cms.SignerInfos.Count == 0 || cms.ContentInfo.ContentType.Value != "1.3.6.1.4.1.311.2.1.4")
                    throw new CryptographicException("Missing signer or unexpected Authenticode content type.");
                cms.CheckSignature(true);
                var (algorithm, expected) = IndirectDigest(cms.ContentInfo.Content);
                byte[] actual = AuthenticodeDigest(image, layout, algorithm);
                bool digestValid = CryptographicOperations.FixedTimeEquals(expected, actual);
                allValid &= digestValid;
                foreach (SignerInfo signer in cms.SignerInfos)
                {
                    var timestamp = InspectTimestamp(signer, cms.Certificates);
                    var certificate = signer.Certificate;
                    var trust = PublisherTrust(certificate, cms.Certificates, expectedPublisher, timestamp.Verified ? timestamp.Time : null);
                    bool trusted = digestValid && timestamp.Verified && trust.Trusted;
                    allTrusted &= trusted;
                    signatures.Add(new { integrityValid = digestValid, digestAlgorithm = algorithm.Name, expectedDigest = Convert.ToHexString(expected).ToLowerInvariant(),
                        actualDigest = Convert.ToHexString(actual).ToLowerInvariant(), certificate = CertificateFacts(certificate),
                        timestamp, trust, nestedSignaturesExamined = false });
                }
                int aligned = checked((length + 7) & ~7);
                if ((long)position + aligned > end) throw new InvalidDataException("WIN_CERTIFICATE padding exceeds table.");
                if (Slice(image, position + length, aligned - length).IndexOfAnyExcept((byte)0) >= 0)
                    throw new InvalidDataException("Nonzero WIN_CERTIFICATE padding.");
                position += aligned;
            }
            return new(!allValid ? "gap" : allTrusted ? "verified" : Untested,
                !allValid ? "authenticode-file-digest-mismatch" : allTrusted ? "publisher-chain-and-timestamp-valid" : "offline-integrity-valid-trust-not-established",
                new { signatures, integrityValid = allValid, trustEstablished = allTrusted, nestedSignaturesExamined = false });
        }
        catch (Exception ex) when (ex is CryptographicException or AsnContentException or IOException or InvalidDataException or ArgumentException or OverflowException)
        {
            return new("gap", "authenticode-invalid", new { error = ex.Message, signatures, nestedSignaturesExamined = false });
        }
    }

    private static (HashAlgorithmName, byte[]) IndirectDigest(byte[] content)
    {
        var reader = new AsnReader(content, AsnEncodingRules.BER);
        var sequence = reader.ReadSequence();
        sequence.ReadEncodedValue();
        var digestInfo = sequence.ReadSequence();
        var identifier = digestInfo.ReadSequence();
        string oid = identifier.ReadObjectIdentifier();
        if (identifier.HasData) identifier.ReadNull();
        identifier.ThrowIfNotEmpty();
        byte[] digest = digestInfo.ReadOctetString();
        digestInfo.ThrowIfNotEmpty(); sequence.ThrowIfNotEmpty(); reader.ThrowIfNotEmpty();
        HashAlgorithmName algorithm = oid switch
        {
            "1.3.14.3.2.26" => HashAlgorithmName.SHA1, "2.16.840.1.101.3.4.2.1" => HashAlgorithmName.SHA256,
            "2.16.840.1.101.3.4.2.2" => HashAlgorithmName.SHA384, "2.16.840.1.101.3.4.2.3" => HashAlgorithmName.SHA512,
            _ => throw new CryptographicException($"Unsupported Authenticode digest OID: {oid}")
        };
        return (algorithm, digest);
    }
    private static byte[] AuthenticodeDigest(byte[] image, PeLayout layout, HashAlgorithmName algorithm)
    {
        if (layout.SecurityDirectoryOffset < 0) throw new InvalidDataException("No PE security-directory slot.");
        using var hash = IncrementalHash.CreateHash(algorithm);
        hash.AppendData(Slice(image, 0, layout.ChecksumOffset));
        int afterChecksum = layout.ChecksumOffset + 4;
        hash.AppendData(Slice(image, afterChecksum, layout.SecurityDirectoryOffset - afterChecksum));
        int afterSecurity = layout.SecurityDirectoryOffset + 8;
        hash.AppendData(Slice(image, afterSecurity, layout.SizeOfHeaders - afterSecurity));
        int sum = layout.SizeOfHeaders;
        foreach (var section in layout.Sections.OrderBy(s => s.Offset))
        {
            hash.AppendData(Slice(image, section.Offset, section.Size));
            sum = checked(sum + section.Size);
        }
        // PE Authenticode's SUM_OF_BYTES_HASHED includes raw sizes, not RVAs.
        // Exclude the complete certificate table, including aligned padding.
        if (sum > layout.CertificateOffset) throw new InvalidDataException("Ambiguous Authenticode overlay bounds.");
        hash.AppendData(Slice(image, sum, layout.CertificateOffset - sum));
        int afterCertificate = checked(layout.CertificateOffset + layout.CertificateSize);
        if (afterCertificate != image.Length)
            throw new InvalidDataException("Trailing data after the Authenticode certificate table is not accepted.");
        return hash.GetHashAndReset();
    }

    private static TimestampEvidence InspectTimestamp(SignerInfo signer, X509Certificate2Collection certificates)
    {
        var observations = new List<object>();
        foreach (CryptographicAttributeObject attribute in signer.UnsignedAttributes)
        {
            if (attribute.Oid.Value is not "1.3.6.1.4.1.311.3.3.1" and not "1.2.840.113549.1.9.16.2.14") continue;
            foreach (AsnEncodedData value in attribute.Values)
            {
                try
                {
                    if (!Rfc3161TimestampToken.TryDecode(value.RawData, out var token, out int consumed) ||
                        token is null || consumed != value.RawData.Length)
                        throw new CryptographicException("Invalid RFC3161 token.");
                    bool signatureValid = token.VerifySignatureForSignerInfo(signer, out var timestampSigner, certificates);
                    var time = token.TokenInfo.Timestamp;
                    var timestampCms = token.AsSignedCms();
                    var chain = ChainTrust(timestampSigner, timestampCms.Certificates, time, "1.3.6.1.5.5.7.3.8");
                    observations.Add(new { kind = "RFC3161", time, signatureValid, certificate = CertificateFacts(timestampSigner), chain });
                    if (signatureValid && chain.Trusted && time <= DateTimeOffset.UtcNow.AddMinutes(5)) return new(true, time, observations,
                        "Timestamp imprint, signature, timestamping EKU and offline OS chain verified.");
                }
                catch (Exception ex) when (ex is CryptographicException or AsnContentException or ArgumentException)
                { observations.Add(new { kind = "RFC3161", error = ex.Message }); }
            }
        }
        foreach (SignerInfo counter in signer.CounterSignerInfos)
        {
            var times = counter.SignedAttributes.Cast<CryptographicAttributeObject>()
                .Where(a => a.Oid.Value == "1.2.840.113549.1.9.5")
                .SelectMany(a => a.Values.Cast<AsnEncodedData>()).Select(v =>
                {
                    try { return new Pkcs9SigningTime(v.RawData).SigningTime.ToUniversalTime().ToString("O"); }
                    catch (CryptographicException) { return "[invalid signing time]"; }
                }).ToArray();
            observations.Add(new { kind = "legacy-countersignature", times, certificate = CertificateFacts(counter.Certificate),
                verified = false, note = "Legacy timestamp trust not established by this inspector." });
        }
        return new(false, null, observations, "No independently verified trusted timestamp; signingTime alone is not a trusted timestamp.");
    }
    private static TrustEvidence PublisherTrust(X509Certificate2? certificate, X509Certificate2Collection extra,
        string? expected, DateTimeOffset? timestamp)
    {
        string expectedIdentity = expected?.Trim() ?? "";
        bool identity = certificate is not null && expectedIdentity.Length > 0 &&
            (certificate.Subject.Contains(expectedIdentity, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(expectedIdentity, certificate.GetNameInfo(X509NameType.SimpleName, false), StringComparison.OrdinalIgnoreCase) ||
             string.Equals(expectedIdentity.Replace(" ", "", StringComparison.Ordinal), certificate.Thumbprint, StringComparison.OrdinalIgnoreCase));
        var chain = ChainTrust(certificate, extra, timestamp ?? DateTimeOffset.UtcNow, "1.3.6.1.5.5.7.3.3");
        return new(identity && chain.Trusted && timestamp is not null, identity, chain.ChainStatus,
            identity ? certificate?.Subject : null,
            "Subject-substring matching is a weak caller-supplied identity binding. A trusted OS chain, code-signing EKU, and verified timestamp remain mandatory.");
    }
    private static TrustEvidence ChainTrust(X509Certificate2? certificate, X509Certificate2Collection extra,
        DateTimeOffset time, string eku)
    {
        if (certificate is null) return new(false, false, ["signer-certificate-missing"], null, "No signer certificate.");
        using var chain = new X509Chain();
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Offline;
        chain.ChainPolicy.VerificationTime = time.UtcDateTime;
        chain.ChainPolicy.ExtraStore.AddRange(extra);
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid(eku));
        bool explicitEku = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>()
            .Any(e => e.EnhancedKeyUsages.Cast<Oid>().Any(o => o.Value == eku));
        try
        {
            bool valid = chain.Build(certificate) && explicitEku;
            return new(valid, explicitEku, chain.ChainStatus.Select(s => $"{s.Status}: {s.StatusInformation.Trim()}").ToArray(), null,
                "Offline OS chain; revocation information may be unavailable. An explicit matching EKU is required.");
        }
        catch (CryptographicException ex)
        {
            return new(false, explicitEku, [ex.Message], null, "Chain construction failed; this does not invalidate CMS/file-digest integrity.");
        }
    }
    private static object? CertificateFacts(X509Certificate2? certificate) => certificate is null ? null : new
    {
        certificate.Subject, certificate.Issuer, certificate.Thumbprint,
        notBefore = certificate.NotBefore.ToUniversalTime(), notAfter = certificate.NotAfter.ToUniversalTime(),
        eku = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>()
            .SelectMany(e => e.EnhancedKeyUsages.Cast<Oid>()).Select(o => new { o.Value, o.FriendlyName }).ToArray()
    };

    private static async Task<PackageSignatureEvidence> InspectPackageSignatureAsync(Archive package)
    {
        var entry = FindEntry(package, ".signature.p7s");
        if (entry is null) return new(false, "missing", false, null, null, null, "Package has no .signature.p7s.");
        string signerType = "invalid";
        bool hasRepositorySignature = false;
        object? signers = null, repositoryFacts = null;
        try
        {
            using var signatureStream = new MemoryStream(entry.Bytes, false);
            var primary = PrimarySignature.Load(signatureStream);
            signerType = primary.Type.ToString();
            var repository = RepositoryCountersignature.GetRepositoryCountersignature(primary);
            bool repositoryPrimary = primary.Type == SignatureType.Repository;
            hasRepositorySignature = repositoryPrimary || repository is not null;
            signers = primary.SignedCms.SignerInfos.Cast<SignerInfo>().Select(s => new
            {
                certificate = CertificateFacts(s.Certificate), timestamp = InspectTimestamp(s, primary.SignedCms.Certificates)
            }).ToArray();
            repositoryFacts = repository is null ? null : new
            {
                certificate = CertificateFacts(repository.SignerInfo.Certificate),
                timestamp = InspectTimestamp(repository.SignerInfo, primary.SignedCms.Certificates)
            };
            primary.SignedCms.CheckSignature(true);
            using var packageReader = new PackageArchiveReader(new MemoryStream(package.Bytes, false));
            await packageReader.ValidateIntegrityAsync(primary.SignatureContent, CancellationToken.None);
            return new(true, signerType, hasRepositorySignature, signers, repositoryFacts,
                new { integrity = "CMS CheckSignature(true) plus NuGet package archive ValidateIntegrityAsync",
                    trustEstablished = false, note = "Neither certificate identity nor a repository-signature type proves NuGet.org origin." }, null);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or InvalidDataException or ArgumentException or
            InvalidOperationException or NuGet.Packaging.Core.PackagingException or SignatureException)
        {
            return new(false, signerType, hasRepositorySignature, signers, repositoryFacts, null, ex.Message);
        }
    }

    private static JsonDocument ParseJson(byte[] bytes) => JsonDocument.Parse(bytes,
        new JsonDocumentOptions { MaxDepth = 64, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
    private static JsonDocument ReadJson(string path) => ParseJson(ReadFile(path, MaxEntryBytes));
    private static string Text(JsonElement node, string key) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
    private static JsonElement Property(JsonElement node, string key) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var value) ? value : default;
    private static IEnumerable<JsonElement> Array(JsonElement node, string key)
    {
        var value = Property(node, key);
        return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : Enumerable.Empty<JsonElement>();
    }
    private static bool Bool(JsonElement node, string key) => Property(node, key).ValueKind == JsonValueKind.True;
    private static string EffectiveLicense(string declared, string concluded) =>
        string.IsNullOrWhiteSpace(declared) || declared is "NOASSERTION" or "NONE"
            ? concluded : declared;
    private static SbomEvidence ReadSbom(string path)
    {
        byte[] bytes = ReadFile(path, MaxArchiveBytes);
        if (bytes.Length >= 2 && bytes[0] == 'P' && bytes[1] == 'K')
        {
            var archive = ReadArchive(path);
            var files = archive.Entries.Where(e => e.Name.EndsWith(".spdx.json", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (files.Length != 1)
                throw new InvalidDataException("SBOM ZIP must contain a single *.spdx.json file.");
            bytes = files[0].Bytes;
        }
        if (bytes.Length > MaxEntryBytes) throw new InvalidDataException("SBOM JSON exceeds limit.");
        using var document = ParseJson(bytes);
        var root = document.RootElement;
        var observedSha256Digests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bindingSha256Digests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void CollectSha256(JsonElement node, int depth)
        {
            if (depth > 64) throw new InvalidDataException("SBOM nesting exceeds limit.");
            if (node.ValueKind == JsonValueKind.String)
            {
                string value = node.GetString() ?? "";
                if (Regex.IsMatch(value, @"\A[a-fA-F0-9]{64}\z")) observedSha256Digests.Add(value.ToLowerInvariant());
            }
            else if (node.ValueKind == JsonValueKind.Array)
                foreach (var child in node.EnumerateArray()) CollectSha256(child, depth + 1);
            else if (node.ValueKind == JsonValueKind.Object)
                foreach (var property in node.EnumerateObject()) CollectSha256(property.Value, depth + 1);
        }
        CollectSha256(root, 0);
        var packages = new List<SbomPackage>();
        var filesEvidence = new List<SbomFile>();
        string format;
        if (Text(root, "spdxVersion") is "SPDX-2.2" or "SPDX-2.3")
        {
            format = Text(root, "spdxVersion");
            var rootIds = Array(root, "documentDescribes").Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString() ?? "").ToHashSet(StringComparer.Ordinal);
            foreach (var relationship in Array(root, "relationships").Where(r =>
                Text(r, "relationshipType").Equals("DESCRIBES", StringComparison.OrdinalIgnoreCase)))
            {
                string described = Text(relationship, "relatedSpdxElement");
                if (!string.IsNullOrWhiteSpace(described)) rootIds.Add(described);
            }
            static IEnumerable<string> Checksums(JsonElement node) => Array(node, "checksums")
                .Where(c => Text(c, "algorithm").Equals("SHA256", StringComparison.OrdinalIgnoreCase))
                .Select(c => Text(c, "checksumValue")).Where(v => Regex.IsMatch(v, @"\A[a-fA-F0-9]{64}\z"));
            foreach (var package in Array(root, "packages"))
            {
                string purl = Array(package, "externalRefs").Select(r => Text(r, "referenceLocator"))
                    .FirstOrDefault(v => v.StartsWith("pkg:", StringComparison.Ordinal)) ?? "";
                string declared = Text(package, "licenseDeclared");
                string concluded = Text(package, "licenseConcluded");
                string expression = EffectiveLicense(declared, concluded);
                string spdxId = Text(package, "SPDXID");
                bool isRoot = rootIds.Contains(spdxId);
                if (isRoot) foreach (string digest in Checksums(package)) bindingSha256Digests.Add(digest);
                packages.Add(new(Text(package, "name"), Text(package, "versionInfo"), purl,
                    spdxId, isRoot, declared, concluded, expression,
                    LicensePolicy.Evaluate(declared), LicensePolicy.Evaluate(concluded),
                    LicensePolicy.EvaluatePair(declared, concluded)));
            }
            foreach (var file in Array(root, "files"))
            {
                string sha = Checksums(file).FirstOrDefault() ?? "";
                string declared = Text(file, "licenseDeclared");
                string concluded = Text(file, "licenseConcluded");
                string expression = EffectiveLicense(declared, concluded);
                string spdxId = Text(file, "SPDXID");
                bool isRoot = rootIds.Contains(spdxId);
                if (isRoot) foreach (string digest in Checksums(file)) bindingSha256Digests.Add(digest);
                filesEvidence.Add(new(Text(file, "fileName"), sha.ToLowerInvariant(), spdxId, isRoot,
                    declared, concluded, expression, LicensePolicy.Evaluate(declared), LicensePolicy.Evaluate(concluded),
                    LicensePolicy.EvaluatePair(declared, concluded)));
            }
        }
        else if (Text(root, "bomFormat") == "CycloneDX" && !string.IsNullOrWhiteSpace(Text(root, "specVersion")))
        {
            format = "CycloneDX-" + Text(root, "specVersion");
            var metadataComponent = Property(Property(root, "metadata"), "component");
            foreach (var hash in Array(metadataComponent, "hashes").Where(h =>
                Text(h, "alg").Equals("SHA-256", StringComparison.OrdinalIgnoreCase))
                .Select(h => Text(h, "content")).Where(v => Regex.IsMatch(v, @"\A[a-fA-F0-9]{64}\z")))
                bindingSha256Digests.Add(hash);
            void Components(JsonElement node, int depth)
            {
                if (depth > 64) throw new InvalidDataException("CycloneDX component nesting exceeds limit.");
                foreach (var component in Array(node, "components"))
                {
                    var expressions = Array(component, "licenses").Select(l => !string.IsNullOrEmpty(Text(l, "expression")) ?
                        Text(l, "expression") : Text(Property(l, "license"), "id"))
                        .Where(e => !string.IsNullOrWhiteSpace(e)).ToArray();
                    string expression = string.Join(" AND ", expressions.Select(e => $"({e})"));
                    string name = Text(component, "name");
                    string purl = Text(component, "purl");
                    var policy = LicensePolicy.EvaluatePair(expression, "");
                    packages.Add(new(name, Text(component, "version"), purl, "", false,
                        expression, "", expression, LicensePolicy.Evaluate(expression), LicensePolicy.Evaluate(""), policy));
                    if (Text(component, "type") == "file")
                    {
                        string sha = Array(component, "hashes").Where(h => Text(h, "alg") == "SHA-256")
                            .Select(h => Text(h, "content")).FirstOrDefault() ?? "";
                        filesEvidence.Add(new(name, sha.ToLowerInvariant(), "", false, expression, "", expression,
                            LicensePolicy.Evaluate(expression), LicensePolicy.Evaluate(""), policy));
                    }
                    Components(component, depth + 1);
                }
            }
            Components(root, 0);
        }
        else throw new InvalidDataException("Expected SPDX 2.2/2.3 or CycloneDX JSON.");
        return new(format, packages, filesEvidence, packages.Count(p => p.Purl.StartsWith("pkg:npm/", StringComparison.Ordinal)),
            packages.Count(p => p.Purl.StartsWith("pkg:nuget/", StringComparison.OrdinalIgnoreCase)),
            observedSha256Digests.Order(StringComparer.Ordinal).ToArray(),
            bindingSha256Digests.Order(StringComparer.Ordinal).ToArray());
    }

    private static object ReadLock(string path)
    {
        using var document = ReadJson(path);
        var root = document.RootElement;
        var packages = new List<object>();
        int runtime = 0, dev = 0, typeOnly = 0;
        var entries = Property(root, "packages");
        if (entries.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in entries.EnumerateObject())
            {
                if (property.Name == "") continue;
                string name = Text(property.Value, "name");
                if (name == "")
                {
                    int marker = property.Name.LastIndexOf("node_modules/", StringComparison.Ordinal);
                    name = marker >= 0 ? property.Name[(marker + "node_modules/".Length)..] : property.Name;
                }
                bool types = name.StartsWith("@types/", StringComparison.Ordinal);
                bool development = Bool(property.Value, "dev") || Bool(property.Value, "devOptional");
                if (types) typeOnly++; else if (development) dev++; else runtime++;
                packages.Add(new { path = property.Name, name, version = Text(property.Value, "version"),
                    category = types ? "type-only" : development ? "dev" : "runtime", optional = Bool(property.Value, "optional"),
                    integrity = Text(property.Value, "integrity"), license = Text(property.Value, "license") });
            }
        }
        else if (Property(root, "dependencies").ValueKind == JsonValueKind.Object)
        {
            void Dependencies(JsonElement dependencies, string prefix, bool nuget, int depth)
            {
                if (depth > 64) throw new InvalidDataException("Lockfile nesting exceeds limit.");
                foreach (var dependency in dependencies.EnumerateObject())
                {
                    var value = dependency.Value;
                    bool isNugetEntry = nuget && Text(value, "resolved") != "";
                    if (isNugetEntry || Text(value, "version") != "")
                    {
                        bool types = dependency.Name.StartsWith("@types/", StringComparison.Ordinal);
                        bool development = Bool(value, "dev");
                        if (types) typeOnly++; else if (development) dev++; else runtime++;
                        packages.Add(new { path = prefix + dependency.Name, name = dependency.Name,
                            version = isNugetEntry ? Text(value, "resolved") : Text(value, "version"),
                            category = types ? "type-only" : development ? "dev" : "runtime",
                            dependencyType = Text(value, "type") });
                    }
                    var children = Property(value, "dependencies");
                    if (children.ValueKind == JsonValueKind.Object && !isNugetEntry)
                        Dependencies(children, prefix + dependency.Name + "/", nuget, depth + 1);
                    if (nuget && !isNugetEntry && value.ValueKind == JsonValueKind.Object)
                        Dependencies(value, prefix + dependency.Name + "/", true, depth + 1);
                }
            }
            Dependencies(Property(root, "dependencies"), "", Property(root, "lockFileVersion").ValueKind == JsonValueKind.Undefined, 0);
        }
        else throw new InvalidDataException("Unsupported package-lock/packages.lock JSON.");
        return new { runtimeCount = runtime, devCount = dev, typeOnlyCount = typeOnly, packages,
            caveat = "Type-only means @types naming, not TypeScript AST analysis. A lockfile alone does not prove bundled dependency coverage." };
    }
    private static Coverage ResolveCoverage(List<WebAsset> web, ResourceAsset[] resources, SbomEvidence? sbom)
    {
        var all = web.Select(w => (w.Path, w.Sha256)).Concat(resources.Select(r => (r.Path, r.Sha256))).ToArray();
        var unresolved = new List<string>();
        bool licensesApproved = true;
        foreach (var asset in all)
        {
            var matches = sbom?.Files.Where(f => f.Path.Replace('\\', '/').TrimStart('.', '/') == asset.Path &&
                f.Sha256.Length == 64 && string.Equals(f.Sha256, asset.Sha256, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
            if (matches.Length != 1 || asset.Sha256.Length != 64) { unresolved.Add(asset.Path); licensesApproved = false; }
            else licensesApproved &= matches[0].Policy.Approved;
        }
        return new(all.Length > 0 && unresolved.Count == 0, licensesApproved && all.Length > 0, unresolved.ToArray(),
            "Exact path plus SHA256 only. File coverage is not proof of SBOM/component completeness or bundled-code lineage.");
    }
    private static Parity CompareParity(Archive package, Archive release)
    {
        var left = package.AllEntries.Where(e => !e.Path.Equals(".signature.p7s", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(e => e.Path, e => e.Sha256, StringComparer.Ordinal);
        var right = release.AllEntries.Where(e => !e.Path.Equals(".signature.p7s", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(e => e.Path, e => e.Sha256, StringComparer.Ordinal);
        string[] differences = left.Keys.Union(right.Keys, StringComparer.Ordinal)
            .Where(k => left.GetValueOrDefault(k) != right.GetValueOrDefault(k)).Order(StringComparer.Ordinal).ToArray();
        return new(differences.Length == 0, left.Count, right.Count, differences, left, right,
            "Only the root .signature.p7s is excluded. Container bytes/signatures are not claimed identical.");
    }

    private static object ReadOfflineAttestation(string path)
    {
        using var document = ReadJson(path);
        var root = document.RootElement;
        string payload = Text(root, "payload");
        JsonElement statement;
        if (payload.Length > 0)
        {
            byte[] decoded;
            try { decoded = Convert.FromBase64String(payload); }
            catch (FormatException ex) { throw new InvalidDataException("Invalid DSSE base64 payload.", ex); }
            using var payloadJson = ParseJson(decoded);
            statement = payloadJson.RootElement.Clone();
        }
        else statement = root.Clone();
        return new { verified = false, payloadType = Text(root, "payloadType"), statement,
            note = "Decoded offline facts only. DSSE signatures, identity, subject binding and provenance are not verified." };
    }

    private static async Task<Provenance> VerifyAttestationAsync(Archive selected, string repository, bool releaseSelected)
    {
        var start = new ProcessStartInfo("gh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true };
        foreach (string argument in new[] { "attestation", "verify", selected.Path, "--repo", repository, "--format", "json" })
            start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try
        {
            if (Hash(ReadFile(selected.Path, MaxArchiveBytes)) != selected.Sha256)
                return new(false, false, "attestation-input-changed", null);
            if (!process.Start()) return new(false, false, "gh-unavailable", null);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            async Task<string> Output(StreamReader reader)
            {
                try
                {
                    char[] buffer = new char[8192];
                    var text = new StringBuilder();
                    int read;
                    while ((read = await reader.ReadAsync(buffer.AsMemory(), timeout.Token)) != 0)
                    {
                        if (text.Length + read > 8 * 1024 * 1024) throw new InvalidDataException("gh output exceeds limit.");
                        text.Append(buffer, 0, read);
                    }
                    return text.ToString();
                }
                catch { await timeout.CancelAsync(); throw; }
            }
            var stdout = Output(process.StandardOutput);
            var stderr = Output(process.StandardError);
            try { await Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdout, stderr); }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            if (Hash(ReadFile(selected.Path, MaxArchiveBytes)) != selected.Sha256)
                return new(false, false, "attestation-input-changed", null);
            if (process.ExitCode != 0)
                return new(false, false, "gh-attestation-verification-failed", new { process.ExitCode, stderr = await stderr });
            using var output = ParseJson(Encoding.UTF8.GetBytes(await stdout));
            var results = output.RootElement.ValueKind == JsonValueKind.Array ? output.RootElement.EnumerateArray().ToArray() : [output.RootElement];
            var statements = results.Select(r => Property(Property(r, "verificationResult"), "statement"))
                .Where(s => s.ValueKind == JsonValueKind.Object).ToArray();
            var bound = statements.Where(s => Array(s, "subject").Any(subject =>
                Text(Property(subject, "digest"), "sha256").Equals(selected.Sha256, StringComparison.OrdinalIgnoreCase))).ToArray();
            bool sourceVerified = false, buildVerified = false;
            var evaluations = new List<object>();
            foreach (var statement in bound)
            {
                bool inToto = Text(statement, "_type") == "https://in-toto.io/Statement/v1";
                bool slsa = Text(statement, "predicateType") == "https://slsa.dev/provenance/v1";
                var predicate = Property(statement, "predicate");
                var definition = Property(predicate, "buildDefinition");
                var workflow = Property(Property(definition, "externalParameters"), "workflow");
                string workflowRepo = Text(workflow, "repository").TrimEnd('/');
                bool repositoryMatches = workflowRepo.Equals("https://github.com/" + repository, StringComparison.OrdinalIgnoreCase);
                bool commitBound = Array(definition, "resolvedDependencies").Any(dependency =>
                {
                    string uri = Text(dependency, "uri");
                    string prefix = "git+https://github.com/" + repository;
                    bool repo = uri.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                        uri.StartsWith(prefix + "@", StringComparison.OrdinalIgnoreCase) || uri.StartsWith(prefix + ".git@", StringComparison.OrdinalIgnoreCase);
                    var digest = Property(dependency, "digest");
                    return repo && new[] { "gitCommit", "sha1", "sha256" }.Any(k =>
                        Regex.IsMatch(Text(digest, k), @"\A(?:[a-fA-F0-9]{40}|[a-fA-F0-9]{64})\z"));
                });
                bool source = inToto && slsa && repositoryMatches && commitBound && !string.IsNullOrWhiteSpace(Text(workflow, "ref"));
                var run = Property(predicate, "runDetails");
                string builder = Text(Property(run, "builder"), "id");
                string invocation = Text(Property(run, "metadata"), "invocationId");
                string workflowPath = Text(workflow, "path");
                string workflowRef = Text(workflow, "ref");
                string expectedWorkflowBuilder = "https://github.com/" + repository + "/" + workflowPath + "@" + workflowRef;
                bool recognizedBuilder = builder == "https://github.com/actions/runner/github-hosted" ||
                    builder.Equals(expectedWorkflowBuilder, StringComparison.OrdinalIgnoreCase);
                bool build = source && Text(definition, "buildType") == "https://actions.github.io/buildtypes/workflow/v1" &&
                    Regex.IsMatch(workflowPath, @"\A\.github/workflows/[A-Za-z0-9_.-]+\.ya?ml\z") &&
                    recognizedBuilder &&
                    Regex.IsMatch(invocation, @"\Ahttps://github\.com/" + Regex.Escape(repository) +
                        @"/actions/runs/[0-9]+(?:/attempts/[0-9]+)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                sourceVerified |= source; buildVerified |= build;
                evaluations.Add(new { statement = statement.Clone(), sourceSemanticsVerified = source, buildSemanticsVerified = build });
            }
            return new(sourceVerified, buildVerified,
                bound.Length == 0 ? "gh-output-missing-exact-sha256-subject" : sourceVerified ? "gh-exact-subject-provenance-verified" : "gh-subject-bound-semantics-unresolved",
                new { selectedPackage = selected.Path, selected.Sha256, releaseSelected, exitCode = process.ExitCode, evaluations,
                    note = releaseSelected ? "Verified the parity-matched pre-repository-signing release artifact itself." :
                        "Verified only the exact selected package. A pre-repository-signing attestation cannot stand in for these bytes." });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidDataException or JsonException or
            OperationCanceledException or InvalidOperationException)
        {
            return new(false, false, "gh-verification-unavailable-or-incomplete", new { error = ex.Message });
        }
    }
}

internal sealed record Row(string Id, string Status, string Basis, string ReasonCode, string Summary,
    bool AgentReviewRecommended, object Facts, bool Deferred = false);
internal sealed record DeferredRow(string Id, string Status, string ReasonCode, string Summary,
    bool AgentReviewRecommended, object Facts, bool Deferred);
internal sealed record Archive(string Path, byte[] Bytes, string Sha256, List<Entry> Entries, List<EntrySummary> AllEntries);
internal sealed record Entry(string Name, byte[] Bytes, string Sha256);
internal sealed record EntrySummary(string Path, int TotalBytes, string Sha256, bool Directory);
internal sealed record CentralEntry(string RawName, string Name, int Local, int End, int Expanded, uint Crc, bool Directory);
internal sealed record Nuspec(string Id, string Version, string Authors, string ProjectUrl, string License,
    string LicenseType, string RepositoryUrl, string RepositoryType, string Commit);
internal sealed record Check(string Status, string ReasonCode, object Facts);
internal sealed record WebAsset(string Path, string Extension, int TotalBytes, string Sha256, string[] LicenseSignals,
    string[] SourceMaps, string[] Sources);
internal sealed record ResourceAsset(string Path, int TotalBytes, string Sha256, string? Error);
internal sealed record LicenseDocument(string Path, string Sha256, string[] Headings, string[] Copyright, string[] LicenseSignals);
internal sealed class PeEvidence(string path)
{
    public string Path { get; } = path;
    public bool Managed { get; set; }
    public string? InformationalVersion { get; set; }
    public Check? StrongName { get; set; }
    public Check? Authenticode { get; set; }
    public List<ResourceAsset> Resources { get; } = [];
    public string? Error { get; set; }
}
internal sealed record TimestampEvidence(bool Verified, DateTimeOffset? Time, List<object> Observations, string Note);
internal sealed record TrustEvidence(bool Trusted, bool IdentityOrEkuMatched, string[] ChainStatus,
    string? MatchedSubject, string Note);
internal sealed record PackageSignatureEvidence(bool Valid, string SignerType, bool HasRepositorySignature,
    object? Signers, object? RepositorySignature, object? Integrity, string? Error);
internal sealed record SbomPackage(string Name, string Version, string Purl, string SpdxId, bool IsRoot,
    string LicenseDeclared, string LicenseConcluded, string EffectiveLicense, LicenseEvaluation DeclaredPolicy,
    LicenseEvaluation ConcludedPolicy, LicenseEvaluation Policy);
internal sealed record SbomFile(string Path, string Sha256, string SpdxId, bool IsRoot, string LicenseDeclared,
    string LicenseConcluded, string EffectiveLicense, LicenseEvaluation DeclaredPolicy,
    LicenseEvaluation ConcludedPolicy, LicenseEvaluation Policy);
internal sealed record SbomEvidence(string Format, List<SbomPackage> Packages, List<SbomFile> Files, int NpmCount,
    int NugetCount, string[] ObservedSha256Digests, string[] BindingSha256Digests);
internal sealed record Coverage(bool Complete, bool LicensesApproved, string[] Unresolved, string Caveat);
internal sealed record Parity(bool Matches, int PackageEntryCount, int ReleaseEntryCount, string[] Differences,
    Dictionary<string, string> PackageEntries, Dictionary<string, string> ReleaseEntries, string Caveat);
internal sealed record Provenance(bool SourceVerified, bool BuildVerified, string ReasonCode, object? Facts);
internal sealed record LicenseEvaluation(bool Approved, string State, string Summary, List<LicenseTerm> Terms);
internal sealed record LicenseTerm(string Identifier, string Category, string? Exception);

internal static class LicensePolicy
{
    // Curated allowlist, not the entire SPDX catalog. Unknown identifiers fail closed.
    private static readonly HashSet<string> Noncopyleft = new(StringComparer.Ordinal)
    {
        "MIT", "MIT-0", "Apache-1.1", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause",
        "0BSD", "ISC", "Zlib", "BSL-1.0", "Unlicense",
        "AFL-1.1", "AFL-1.2", "AFL-2.0", "AFL-2.1", "AFL-3.0", "BlueOak-1.0.0", "PostgreSQL",
        "Python-2.0", "PSF-2.0", "NCSA", "UPL-1.0", "W3C", "X11", "HPND", "ZPL-2.0"
    };
    private static readonly HashSet<string> WeakCopyleft = new(StringComparer.Ordinal)
    {
        "MPL-1.0", "MPL-1.1", "MPL-2.0", "EPL-1.0", "EPL-2.0", "CDDL-1.0", "CDDL-1.1", "CPL-1.0",
        "LGPL-2.0", "LGPL-2.0-only", "LGPL-2.0-or-later", "LGPL-2.1", "LGPL-2.1-only", "LGPL-2.1-or-later",
        "LGPL-3.0", "LGPL-3.0-only", "LGPL-3.0-or-later", "MS-RL", "MS-PL", "Artistic-1.0", "Artistic-2.0"
    };
    private static readonly HashSet<string> StrongCopyleft = new(StringComparer.Ordinal)
    {
        "GPL-1.0", "GPL-1.0-only", "GPL-1.0-or-later", "GPL-2.0", "GPL-2.0-only", "GPL-2.0-or-later",
        "GPL-3.0", "GPL-3.0-only", "GPL-3.0-or-later", "AGPL-1.0", "AGPL-1.0-only", "AGPL-1.0-or-later",
        "AGPL-3.0", "AGPL-3.0-only", "AGPL-3.0-or-later", "EUPL-1.1", "EUPL-1.2", "RPL-1.5", "SSPL-1.0", "OSL-3.0"
    };
    private static readonly HashSet<string> Exceptions = new(StringComparer.Ordinal)
        { "LLVM-exception", "Classpath-exception-2.0", "GCC-exception-3.1", "Bison-exception-2.2", "Autoconf-exception-3.0" };
    private static bool IsPlaceholder(string expression) =>
        string.IsNullOrWhiteSpace(expression) || expression == "NOASSERTION";
    public static LicenseEvaluation Evaluate(string expression)
    {
        if (IsPlaceholder(expression) || expression == "NONE" || expression.Contains("LicenseRef-", StringComparison.Ordinal))
            return new(false, "unresolved", "License evidence is absent, NOASSERTION/NONE, or LicenseRef-only.", []);
        SpdxParser? parser = null;
        try
        {
            parser = new SpdxParser(expression);
            bool approved = parser.Parse();
            bool disallowed = parser.Terms.Any(t => t.Category is "weak-copyleft" or "strong-copyleft");
            return new(approved, approved ? "approved" : disallowed ? "disallowed" : "unresolved",
                approved ? "All terms and alternatives are curated OSI-approved noncopyleft licenses." :
                "Unknown, non-allowlisted or copyleft terms require owner/legal review; OR does not waive an alternative.",
                parser.Terms);
        }
        catch (InvalidDataException ex) { return new(false, "malformed", ex.Message, parser?.Terms ?? []); }
    }
    public static LicenseEvaluation EvaluatePair(string declared, string concluded)
    {
        var fields = new[] { declared, concluded }.Where(v => !IsPlaceholder(v)).Select(Evaluate).ToArray();
        var terms = fields.SelectMany(f => f.Terms).ToList();
        if (fields.Any(f => f.State is "disallowed" or "malformed"))
            return new(false, "disallowed", "At least one non-placeholder declared/concluded field is malformed or explicitly disallowed.", terms);
        if (fields.Length == 0 || fields.Any(f => f.State == "unresolved"))
            return new(false, "unresolved", "Declared/concluded license evidence is absent or unresolved.", terms);
        return new(true, "approved", "All non-placeholder declared/concluded license fields are approved.", terms);
    }
    public static bool IsCopyleftSignal(string text) => Regex.IsMatch(text,
        @"(?:\b(?:A?GPL|LGPL)[-\s]?(?:v?[123](?:\.\d)?)|\bMPL-2\.0\b|GNU (?:AFFERO |LESSER |LIBRARY )?GENERAL PUBLIC LICENSE)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private sealed class SpdxParser
    {
        private readonly List<string> tokens = [];
        private int index;
        public List<LicenseTerm> Terms { get; } = [];
        public SpdxParser(string text)
        {
            if (text.Length > 16384) throw new InvalidDataException("SPDX expression exceeds limit.");
            for (int i = 0; i < text.Length;)
            {
                if (char.IsWhiteSpace(text[i])) { i++; continue; }
                if (text[i] is '(' or ')' or '+') { tokens.Add(text[i++].ToString()); continue; }
                int start = i;
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] is '-' or '.')) i++;
                if (start == i) { tokens.Add("[invalid]"); i++; } else tokens.Add(text[start..i]);
                if (tokens.Count > 2048) throw new InvalidDataException("SPDX token limit exceeded.");
            }
        }
        public bool Parse()
        {
            if (tokens.Count == 0) throw new InvalidDataException("License expression is missing.");
            bool approved = Or(0);
            if (index != tokens.Count) throw new InvalidDataException("Malformed SPDX expression.");
            return approved;
        }
        private bool Take(string token)
        {
            if (index < tokens.Count && tokens[index] == token) { index++; return true; }
            return false;
        }
        private bool Or(int depth)
        {
            bool approved = And(depth);
            while (Take("OR")) approved = And(depth) & approved;
            return approved;
        }
        private bool And(int depth)
        {
            bool approved = Term(depth);
            while (Take("AND")) approved = Term(depth) & approved;
            return approved;
        }
        private bool Term(int depth)
        {
            if (depth > 64) throw new InvalidDataException("SPDX nesting exceeds limit.");
            if (Take("("))
            {
                bool result = Or(depth + 1);
                if (!Take(")")) throw new InvalidDataException("Missing SPDX closing parenthesis.");
                return result;
            }
            if (index == tokens.Count || tokens[index] is "AND" or "OR" or "WITH" or ")" or "+" or "[invalid]")
                throw new InvalidDataException("Expected SPDX license identifier.");
            string id = tokens[index++];
            bool plus = Take("+");
            string? exception = null;
            if (Take("WITH"))
            {
                if (index == tokens.Count || tokens[index] is "(" or ")" or "AND" or "OR" or "WITH" or "+" or "[invalid]")
                    throw new InvalidDataException("Expected SPDX exception identifier.");
                exception = tokens[index++];
            }
            string category = Noncopyleft.Contains(id) && !plus ? "osi-approved-noncopyleft" :
                WeakCopyleft.Contains(id) ? "weak-copyleft" : StrongCopyleft.Contains(id) ? "strong-copyleft" : "unknown-or-nonapproved";
            Terms.Add(new(id + (plus ? "+" : ""), category, exception));
            return category == "osi-approved-noncopyleft" && (exception is null || Exceptions.Contains(exception));
        }
    }
}

internal sealed record PeSection(int Rva, int VirtualSize, int Offset, int Size);
internal sealed record PeLayout(int ChecksumOffset, int SecurityDirectoryOffset, int CertificateOffset,
    int CertificateSize, int SizeOfHeaders, int HeaderEnd, List<PeSection> Sections)
{
    private static int Number(byte[] b, int p)
    {
        if (p < 0 || (long)p + 4 > b.Length) throw new BadImageFormatException("Truncated PE structure.");
        return checked((int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p, 4)));
    }
    private static int Short(byte[] b, int p)
    {
        if (p < 0 || (long)p + 2 > b.Length) throw new BadImageFormatException("Truncated PE structure.");
        return BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p, 2));
    }
    public static PeLayout Read(byte[] image)
    {
        int nt = Number(image, 0x3c);
        if (nt < 64 || Number(image, nt) != 0x4550) throw new BadImageFormatException("Invalid PE signature.");
        int count = Short(image, nt + 6), optionalSize = Short(image, nt + 20), optional = checked(nt + 24);
        int magic = Short(image, optional);
        int directoryRelative = magic switch { 0x10b => 96, 0x20b => 112, _ => throw new BadImageFormatException("Unsupported PE optional header.") };
        if (optionalSize < directoryRelative || count > 96) throw new BadImageFormatException("Invalid PE optional header/section count.");
        int directories = Number(image, optional + directoryRelative - 4);
        if (directories > 16 || optionalSize < directoryRelative + directories * 8)
            throw new BadImageFormatException("Invalid PE data-directory count.");
        int sectionStart = checked(optional + optionalSize), headerEnd = checked(sectionStart + count * 40);
        int headers = Number(image, optional + 60), checksum = checked(optional + 64);
        if (headers < headerEnd || headers > image.Length) throw new BadImageFormatException("Invalid PE header bounds.");
        var sections = new List<PeSection>();
        for (int i = 0; i < count; i++)
        {
            int p = checked(sectionStart + i * 40);
            var section = new PeSection(Number(image, p + 12), Number(image, p + 8), Number(image, p + 20), Number(image, p + 16));
            if (section.Size > 0 && (section.Offset < headers || (long)section.Offset + section.Size > image.Length))
                throw new BadImageFormatException("PE raw section exceeds file.");
            sections.Add(section);
        }
        int previous = headers;
        foreach (var section in sections.Where(s => s.Size > 0).OrderBy(s => s.Offset))
        {
            if (section.Offset < previous) throw new BadImageFormatException("Overlapping PE raw sections.");
            previous = checked(section.Offset + section.Size);
        }
        int security = directories > 4 ? optional + directoryRelative + 32 : -1;
        int certificate = security < 0 ? 0 : Number(image, security), certificateSize = security < 0 ? 0 : Number(image, security + 4);
        if ((certificate == 0) != (certificateSize == 0) || certificateSize > 0 &&
            (certificate % 8 != 0 || certificate < previous || (long)certificate + certificateSize > image.Length))
            throw new BadImageFormatException("Invalid PE certificate-table bounds.");
        return new(checksum, security, certificate, certificateSize, headers, headerEnd, sections);
    }
    public int RvaToOffset(int rva, int size)
    {
        if (rva < 0 || size <= 0) throw new BadImageFormatException("Invalid PE directory RVA/size.");
        var matches = Sections.Where(s => rva >= s.Rva && (long)rva + size <= (long)s.Rva + s.Size).ToArray();
        if (matches.Length != 1) throw new BadImageFormatException("PE directory has no unique raw-section mapping.");
        return checked(matches[0].Offset + rva - matches[0].Rva);
    }
}
internal static class CrcTable
{
    public static readonly uint[] Values = Create();
    private static uint[] Create()
    {
        var values = new uint[256];
        for (uint i = 0; i < values.Length; i++)
        {
            uint crc = i;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
            values[i] = crc;
        }
        return values;
    }
}
