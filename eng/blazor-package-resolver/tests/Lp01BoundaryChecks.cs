using System.IO.Compression;
using System.Text;
using BlazorPackageResolver;

internal static class Lp01BoundaryChecks
{
    internal static void Run(Registry registry, string directory)
    {
        var fixture = Path.Combine(directory, "packages", "mit.nupkg");
        var policy = File.ReadAllBytes(Path.Combine(directory, "policy", "lp01-policy-1.0.0.json"));
        var corpus = File.ReadAllBytes(Path.Combine(directory, "policy", "spdx-licenses-3.27.0.json"));
        byte[] source;
        using (var archive = new ZipArchive(new MemoryStream(File.ReadAllBytes(fixture)), ZipArchiveMode.Read))
        using (var reader = archive.GetEntry("Sample.LP01.nuspec")!.Open())
        using (var buffer = new MemoryStream())
        {
            reader.CopyTo(buffer);
            source = buffer.ToArray();
        }

        void Check(byte[] nuspec, params string[] aliases)
        {
            using var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var writer = archive.CreateEntry("Sample.LP01.nuspec").Open())
                    writer.Write(nuspec);
                foreach (var alias in aliases)
                {
                    using var writer = archive.CreateEntry(alias).Open();
                    writer.Write("passive\n"u8);
                }
            }
            var archiveBytes = buffer.ToArray();
            var identities = new[]
            {
                new PinnedInput("package", "packages/boundary.nupkg", Catalog.Digest(archiveBytes)),
                new PinnedInput("policy", "policy/lp01-policy-1.0.0.json", Catalog.Digest(policy)),
                new PinnedInput("corpus", "policy/spdx-licenses-3.27.0.json", Catalog.Digest(corpus))
            };
            var bytes = new[] { archiveBytes, policy, corpus };
            var inputs = identities.Zip(bytes).ToDictionary(pair => pair.First.Role,
                pair => new CheckedInput(pair.First, pair.Second), StringComparer.Ordinal);
            var result = registry.Dispatch(new ResolverCase("lp01-boundary", "LP-01", identities, inputs));
            if (result.BranchId != "unsafe-package" || result.Applicability != "unknown")
                throw new InvalidDataException("LP-01 must reject unsupported XML and Unicode-casefold ZIP aliases.");
        }

        Check(source, "Stra\u00dfe.txt", "STRASSE.txt");
        Check(source, "\u212a.txt", "k.txt");
        Check(source, "\u0130.txt", "i\u0307.txt");
        var xml = Encoding.UTF8.GetString(source);
        Check(Encoding.UTF8.GetBytes(xml.Replace("encoding=\"utf-8\"", "encoding=\"iso-8859-1\"")));
        Check([0xff, 0xfe, 0, 0, .. Encoding.UTF32.GetBytes(xml)]);
        var ownerPackage = File.ReadAllBytes(Path.Combine(directory, "packages", "owner-accepted.nupkg"));
        var grant = File.ReadAllBytes(Path.Combine(directory, "grants", "mit-full.txt"));
        var record = File.ReadAllText(Path.Combine(directory, "owners", "accepted.json"));
        foreach (var ownerName in new[] { "\\ud83d\\ude00 Owner", "Owner < &" })
        {
            var ownerBytes = Encoding.UTF8.GetBytes(record.Replace("Example Fixture Maintainer", ownerName));
            var ownerIdentities = new[]
            {
                new PinnedInput("package", "packages/owner-accepted.nupkg", Catalog.Digest(ownerPackage)),
                new PinnedInput("policy", "policy/lp01-policy-1.0.0.json", Catalog.Digest(policy)),
                new PinnedInput("corpus", "policy/spdx-licenses-3.27.0.json", Catalog.Digest(corpus)),
                new PinnedInput("owner-record", "owners/owner-variant.json", Catalog.Digest(ownerBytes)),
                new PinnedInput("grant-mit", "grants/mit-full.txt", Catalog.Digest(grant))
            };
            var ownerContents = new[] { ownerPackage, policy, corpus, ownerBytes, grant };
            var ownerInputs = ownerIdentities.Zip(ownerContents).ToDictionary(pair => pair.First.Role,
                pair => new CheckedInput(pair.First, pair.Second), StringComparer.Ordinal);
            var result = registry.Dispatch(new ResolverCase("lp01-owner-unicode", "LP-01",
                ownerIdentities, ownerInputs));
            if (result.BranchId != "owner-evidence-accepted")
                throw new InvalidDataException("A canonical prevalidated owner name may contain safe Unicode or HTML punctuation.");
        }
        Console.WriteLine("LP-01 Unicode aliases, XML encodings and canonical owner names: 7 checks passed.");
    }
}
