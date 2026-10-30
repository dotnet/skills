using System.IO.Compression;
using System.Text;
using System.Text.Json;
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
        Check(Encoding.Unicode.GetBytes(xml.Replace("encoding=\"utf-8\"", "encoding=\"utf-16\"")));
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
        Console.WriteLine("LP-01 Unicode aliases, XML encodings and canonical owner names: 8 checks passed.");
        RunCorrection(registry, directory, policy, corpus, source, ownerPackage, grant, record);
    }

    private static void RunCorrection(Registry registry, string directory, byte[] policy,
        byte[] corpus, byte[] source, byte[] ownerPackage, byte[] grant, string record)
    {
        var ownerNuspec = ReadNuspec(ownerPackage);
        var originalPackageDigest = Catalog.Digest(ownerPackage);
        var originalGrantDigest = Catalog.Digest(grant);
        var checks = 0;

        CaseResult Resolve(byte[] package, byte[]? owner = null, byte[]? checkedGrant = null,
            params string[] extraRoles)
        {
            var identities = new List<PinnedInput>
            {
                new("package", "packages/boundary.nupkg", Catalog.Digest(package)),
                new("policy", "policy/lp01-policy-1.0.0.json", Catalog.Digest(policy)),
                new("corpus", "policy/spdx-licenses-3.27.0.json", Catalog.Digest(corpus))
            };
            var contents = new List<byte[]> { package, policy, corpus };
            if (owner is not null)
            {
                identities.Add(new("owner-record", "owners/boundary.json", Catalog.Digest(owner)));
                contents.Add(owner);
            }
            if (checkedGrant is not null)
            {
                identities.Add(new("grant-mit", "grants/mit-full.txt", Catalog.Digest(checkedGrant)));
                contents.Add(checkedGrant);
            }
            foreach (var role in extraRoles)
            {
                identities.Add(new(role, "extras/" + role + ".txt", Catalog.Digest("extra"u8)));
                contents.Add("extra"u8.ToArray());
            }
            var checkedInputs = identities.Zip(contents).ToDictionary(pair => pair.First.Role,
                pair => new CheckedInput(pair.First, pair.Second), StringComparer.Ordinal);
            return registry.Dispatch(new ResolverCase("lp01-correction-boundary", "LP-01",
                identities, checkedInputs));
        }

        byte[] OwnerFor(byte[] package, byte[]? checkedGrant = null) =>
            Encoding.UTF8.GetBytes(record.Replace(originalPackageDigest, Catalog.Digest(package),
                StringComparison.Ordinal).Replace(originalGrantDigest,
                Catalog.Digest(checkedGrant ?? grant), StringComparison.Ordinal));

        void Expect(string label, string branch, byte[] package, byte[]? owner = null,
            byte[]? checkedGrant = null, params string[] extraRoles)
        {
            var result = Resolve(package, owner, checkedGrant, extraRoles);
            if (result.BranchId != branch)
                throw new InvalidDataException($"LP-01 {label}: expected {branch}, got {result.BranchId}.");
            checks++;
        }

        byte[] Package(byte[] nuspec, Encoding? nameEncoding = null,
            params (string Name, byte[] Content)[] files)
        {
            using var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true,
                entryNameEncoding: nameEncoding))
            {
                using (var stream = archive.CreateEntry("Sample.LP01.nuspec").Open())
                    stream.Write(nuspec);
                foreach (var (name, content) in files)
                    using (var stream = archive.CreateEntry(name).Open())
                        stream.Write(content);
            }
            return buffer.ToArray();
        }

        static int Header(byte[] bytes, uint signature, string name, bool local)
        {
            var encoded = Encoding.ASCII.GetBytes(name);
            var nameOffset = local ? 30 : 46;
            for (var index = 0; index + nameOffset + encoded.Length <= bytes.Length; index++)
            {
                if (BitConverter.ToUInt32(bytes, index) == signature &&
                    bytes.AsSpan(index + nameOffset, encoded.Length).SequenceEqual(encoded))
                    return index;
            }
            throw new InvalidDataException("Test ZIP header not found: " + name);
        }

        var cp437 = Encoding.GetEncoding(437);
        Expect("valid CP437 entry", "approved-non-copyleft",
            Package(source, cp437, ("Stra\u00dfe.txt", "a"u8.ToArray())));
        Expect("CP437 casefold alias", "unsafe-package", Package(source, cp437,
            ("Stra\u00dfe.txt", "a"u8.ToArray()), ("STRASSE.txt", "b"u8.ToArray())));
        var utf8 = Package(source, Encoding.UTF8, ("caf\u00e9.txt", "a"u8.ToArray()));
        for (var index = 0; index < utf8.Length - 4; index++)
            if (utf8[index] == (byte)'c' && utf8[index + 1] == (byte)'a' &&
                utf8[index + 2] == (byte)'f' && utf8[index + 3] == 0xc3)
                utf8[index + 3] = 0xff;
        Expect("invalid flagged UTF-8", "unsafe-package", utf8);
        var nullName = Package(source, null, ("LICENSE.txt", "a"u8.ToArray()));
        nullName[Header(nullName, 0x04034b50, "LICENSE.txt", true) + 30] = 0;
        nullName[Header(nullName, 0x02014b50, "LICENSE.txt", false) + 46] = 0;
        Expect("NUL archive name", "unsafe-package", nullName);
        var localMismatch = Package(source, null, ("LICENSE.txt", "a"u8.ToArray()));
        localMismatch[Header(localMismatch, 0x04034b50, "LICENSE.txt", true) + 30] = (byte)'X';
        Expect("local and central name mismatch", "unsafe-package", localMismatch);
        foreach (var name in new[] { "Sample.LP01.nuspec", "LICENSE.txt" })
        {
            var corrupted = Package(source, null, ("LICENSE.txt", "a"u8.ToArray()));
            corrupted[Header(corrupted, 0x02014b50, name, false) + 16] ^= 0x01;
            Expect("CRC " + name, "unsafe-package", corrupted);
        }
        var localCrcMismatch = Package(source, null, ("LICENSE.txt", "a"u8.ToArray()));
        localCrcMismatch[Header(localCrcMismatch, 0x04034b50, "LICENSE.txt", true) + 14] ^= 0x01;
        Expect("local and central CRC mismatch", "unsafe-package", localCrcMismatch);

        foreach (var version in new[] { "1.0.0.0", "0.999", "00001.0",
            "+1.0", "-1.0", " 1.0", "1.0 ", "1. 0", "1..0", "1.0.0.0.0" })
        {
            var xml = Encoding.UTF8.GetString(source).Replace(
                "type=\"expression\"", "type=\"expression\" version=\"" + version + "\"",
                StringComparison.Ordinal);
            var expected = version is "1.0.0.0" or "0.999" or "00001.0"
                ? "approved-non-copyleft" : "unsafe-metadata";
            Expect("license version " + version, expected, Package(Encoding.UTF8.GetBytes(xml)));
        }
        var gpl = Package(Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(source).Replace(">MIT</license>", ">GPL-3.0-only</license>",
                StringComparison.Ordinal)));
        Expect("unknown role precedes GPL", "owner-record-invalid", gpl, extraRoles: ["rogue"]);
        var noOwnerPackage = Package(source);
        var withoutExtras = Resolve(noOwnerPackage);
        foreach (var (label, withExtras) in new[]
        {
            ("valid extra grant", Resolve(noOwnerPackage, checkedGrant: grant)),
            ("valid extra conflict record", Resolve(noOwnerPackage,
                extraRoles: ["owner-record-conflict"]))
        })
        {
            if (withExtras.BranchId != "approved-non-copyleft" ||
                withExtras.Decision != withoutExtras.Decision ||
                withExtras.Applicability != withoutExtras.Applicability ||
                !withExtras.EvidenceReferences.SequenceEqual(withoutExtras.EvidenceReferences) ||
                withExtras.EvidenceContract != withoutExtras.EvidenceContract ||
                withExtras.MissingFact != withoutExtras.MissingFact ||
                withExtras.RequiredInput != withoutExtras.RequiredInput ||
                withExtras.NextCategory != withoutExtras.NextCategory)
                throw new InvalidDataException("LP-01 no-owner result depends on " + label);
            checks++;
        }

        var canonicalOwner = OwnerFor(ownerPackage);
        Expect("lone surrogate top-level key", "owner-record-invalid", ownerPackage,
            Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(canonicalOwner)
                .Replace("\"owner_identity\"", "\"\\ud800\"", StringComparison.Ordinal)), grant);
        Expect("lone surrogate nested key", "owner-record-invalid", ownerPackage,
            Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(canonicalOwner)
                .Replace("\"license_id\"", "\"\\ud800\"", StringComparison.Ordinal)), grant);
        foreach (var (key, oldValue, replacement) in new[]
        {
            ("id", "<id>Sample.LP01</id>", "<id>Sample.<part>LP</part>01</id>"),
            ("version", "<version>1.0.0</version>", "<version>1.<part>0</part>.0</version>")
        })
        {
            var mixed = Package(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(ownerNuspec)
                .Replace(oldValue, replacement, StringComparison.Ordinal)));
            Expect("owner mixed-content " + key, "owner-binding-conflict",
                mixed, OwnerFor(mixed), grant);
        }
        var tagged = Encoding.UTF8.GetBytes("SPDX-License-Identifier: MIT\n\u001c\u001d\u001e\u001f");
        var untagged = Encoding.UTF8.GetBytes("\u001c\u001d\u001e\u001f");
        Expect("tagged Python whitespace grant", "grant-incomplete",
            ownerPackage, OwnerFor(ownerPackage, tagged), tagged);
        Expect("untagged Python whitespace grant", "grant-incomplete",
            ownerPackage, OwnerFor(ownerPackage, untagged), untagged);
        var separatedContradiction = Encoding.UTF8.GetBytes(
            "SPDX-License-Identifier: GPL-3.0-only\u001cComplete grant text");
        Expect("unsupported declaration separator", "grant-incomplete",
            ownerPackage, OwnerFor(ownerPackage, separatedContradiction), separatedContradiction);
        var whitespaceFile = Package(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(source)
            .Replace("type=\"expression\"> MIT ", "type=\"file\">COPYING", StringComparison.Ordinal)),
            null, ("COPYING", "SPDX-License-Identifier: MIT\n\u001c\u001d\u001e\u001f"u8.ToArray()));
        Expect("file declaration Python whitespace", "file-declared", whitespaceFile);
        var copying = Package(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(source)
            .Replace("type=\"expression\"> MIT ", "type=\"file\">COPYING", StringComparison.Ordinal)),
            null, ("COPYING", "SPDX-License-Identifier: MIT\n"u8.ToArray()));
        var fileResult = Resolve(copying);
        if (fileResult.BranchId != "file-declared" ||
            !fileResult.EvidenceReferences.Contains("package:COPYING", StringComparer.Ordinal) ||
            fileResult.EvidenceReferences.Any(reference => reference == "package:LICENSE.txt") ||
            fileResult.EvidenceContract is null ||
            !fileResult.EvidenceContract.Contains("Checked COPYING", StringComparison.Ordinal))
            throw new InvalidDataException($"LP-01 must cite the actual safely inspected license file: {fileResult.BranchId}; " +
                string.Join(", ", fileResult.EvidenceReferences) + "; " + fileResult.EvidenceContract);
        checks++;
        Console.WriteLine($"LP-01 Phase B supplemental ZIP, grant, version, roles, owner and file: {checks} checks passed.");
    }

    private static byte[] ReadNuspec(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var stream = archive.Entries.Single(entry =>
            entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }
}
