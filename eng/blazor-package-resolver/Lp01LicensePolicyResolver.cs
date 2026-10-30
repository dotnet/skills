using System.Buffers;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BlazorPackageResolver;

public sealed class Lp01LicensePolicyResolver : IDeterministicResolver
{
    private const string CorpusDigest = "157789ba91984aecba5c67b84168dbbd41a235b910a7569aa64bf42a88b24290";
    private const string PolicyDigest = "b395256c51e7c0ae68bf3a213aab90bee0479f6115c3507e456269d0cdd41f47";
    private const string OwnerContract =
        "Available checked package, policy, owner-record and grant inputs were inspected; missing or conflicting evidence cannot establish owner authority or license scope.";
    private const string AcceptedContract =
        "Checked package/nuspec, SPDX 3.27.0, policy 1.0.0, canonical prevalidated owner record and one separately checked, caller-accepted complete grant per distinct normalized SPDX license ID bind exactly; owner authority, release correspondence and legal grant sufficiency are trusted at the input boundary, not cryptographically proven or legally interpreted by resolver.";
    private const string PackageContract =
        "Checked package SPDX expression: all alternatives OSI-approved by SPDX 3.27.0 and non-copyleft under pinned policy 1.0.0; owner grant and release/source scope not established.";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Regex Hex = new(@"\A[a-f0-9]{64}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Commit = new(@"\A(?:[a-f0-9]{40}|[a-f0-9]{64})\z", RegexOptions.CultureInvariant);
    private static readonly Regex SafePath = new(@"\A[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)*\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex GrantRole = new(@"\Agrant-[a-z0-9][a-z0-9.+-]*\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex Declaration = new(
        @"\A[ \t]*SPDX-License-Identifier:[ \t]*([A-Za-z0-9][A-Za-z0-9.+-]*)[ \t]*\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex XmlEncoding = new(
        "\\A<\\?xml\\s+[^?]*?\\bencoding\\s*=\\s*(['\"])([^'\"]+)\\1",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly string[] OwnerFields =
    [
        "schema_version", "caller_accepts_prevalidated_authority", "owner_identity",
        "owner_reference", "package_id", "package_sha256", "repository_url",
        "source_commit", "release_ref", "release_version", "spdx_grant_expression",
        "grant_bindings", "prevalidation_references", "policy_version", "policy_sha256",
        "spdx_version", "spdx_sha256"
    ];
    private static readonly string[] BindingFields =
        ["license_id", "role", "path", "sha256", "caller_accepts_complete_grant"];
    private static readonly string[] PolicyFields =
        ["policy_version", "spdx_version", "spdx_sha256",
         "non_copyleft_allowlist", "copyleft", "weak_copyleft"];
    private sealed record Policy(
        IReadOnlyDictionary<string, (bool Osi, bool Deprecated)> Licenses,
        HashSet<string> Allowed, HashSet<string> Strong, HashSet<string> Weak);
    private sealed record PackageState(string Branch, XElement? Metadata = null,
        Lp01SpdxExpression? Expression = null, string? LicenseFilePath = null);

    public string RowId => "LP-01";
    public string ResolverId => "lp01-license-policy";
    public string Version => "1.0.0";

    public CaseResult Resolve(ResolverCase input)
    {
        if (!input.CheckedInputs.TryGetValue("package", out var package))
            return Result(input, "missing-package");
        if (!input.CheckedInputs.TryGetValue("policy", out var policyBytes))
            return Result(input, "missing-policy");
        if (!input.CheckedInputs.TryGetValue("corpus", out var catalogBytes))
            return Result(input, "missing-corpus");
        if (input.Inputs.Any(i => !IsSafePath(i.Path) ||
            i.Role is not ("package" or "policy" or "corpus" or
                           "owner-record" or "owner-record-conflict") &&
            !GrantRole.IsMatch(i.Role)))
            return Result(input, "owner-record-invalid");
        var policy = ReadPolicy(policyBytes.Content.Span, catalogBytes.Content.Span);
        if (policy is null) return Result(input, "invalid-policy");
        var state = InspectPackage(package.Content, policy);
        if (state.Branch != "approved-non-copyleft")
            return Result(input, state.Branch, state.LicenseFilePath);
        return Result(input, InspectOwner(input, package, policyBytes, state), state.LicenseFilePath);
    }

    private static Policy? ReadPolicy(ReadOnlySpan<byte> policyBytes, ReadOnlySpan<byte> catalogBytes)
    {
        if (policyBytes.Length > 8192 || Catalog.Digest(policyBytes) != PolicyDigest ||
            catalogBytes.Length > 2 * 1024 * 1024 ||
            Catalog.Digest(catalogBytes) != CorpusDigest)
            return null;
        try
        {
            using var policyDoc = JsonDocument.Parse(policyBytes.ToArray());
            using var catalogDoc = JsonDocument.Parse(catalogBytes.ToArray());
            var source = policyDoc.RootElement;
            var catalog = catalogDoc.RootElement;
            if (!Fields(source, PolicyFields, ordered: false) ||
                Text(source, "policy_version") != "1.0.0" ||
                Text(source, "spdx_version") != "3.27.0" ||
                Text(source, "spdx_sha256") != CorpusDigest ||
                Text(catalog, "licenseListVersion") != "3.27.0" ||
                !catalog.TryGetProperty("licenses", out var list) || list.ValueKind != JsonValueKind.Array)
                return null;
            var licenses = new Dictionary<string, (bool, bool)>(StringComparer.Ordinal);
            foreach (var license in list.EnumerateArray())
            {
                var id = Text(license, "licenseId");
                if (id is null || !license.TryGetProperty("isOsiApproved", out var osi) ||
                    osi.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                    !license.TryGetProperty("isDeprecatedLicenseId", out var deprecated) ||
                    deprecated.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                    !licenses.TryAdd(id, (osi.GetBoolean(), deprecated.GetBoolean())))
                    return null;
            }
            HashSet<string>? Group(string key)
            {
                if (!source.TryGetProperty(key, out var array) || array.ValueKind != JsonValueKind.Array)
                    return null;
                var result = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in array.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String ||
                        !result.Add(item.GetString()!)) return null;
                }
                return result;
            }
            var allowed = Group("non_copyleft_allowlist");
            var strong = Group("copyleft");
            var weak = Group("weak_copyleft");
            if (allowed is null || strong is null || weak is null ||
                allowed.Count + strong.Count + weak.Count !=
                    allowed.Concat(strong).Concat(weak).Distinct(StringComparer.Ordinal).Count() ||
                allowed.Concat(strong).Concat(weak).Any(id =>
                    !licenses.TryGetValue(id, out var item) || !item.Item1 || item.Item2))
                return null;
            return new(licenses, allowed, strong, weak);
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static PackageState InspectPackage(ReadOnlyMemory<byte> content, Policy policy)
    {
        if (!NuGetNuspecReader.HasBoundedDirectory(content.Span, out var count))
            return new("unsafe-package");
        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read,
                leaveOpen: false, entryNameEncoding: Lp01ArchiveIntegrity.Cp437);
            if (archive.Entries.Count != count ||
                !Lp01ArchiveIntegrity.Validate(content.Span, archive.Entries))
                return new("unsafe-package");
            var nuspecs = archive.Entries.Where(e => e.FullName.EndsWith(".nuspec",
                StringComparison.OrdinalIgnoreCase) && !e.FullName.Contains('/') &&
                !e.FullName.Contains('\\')).ToArray();
            if (nuspecs.Length == 1)
            {
                if (!TryRead(nuspecs[0], 1024 * 1024, out var xml) || !SupportedXmlEncoding(xml))
                    return new("unsafe-package");
            }
            var read = NuGetNuspecReader.Read(content);
            if (read.Status != NuspecStatus.Ready || read.Metadata is null)
                return new("unsafe-package");
            var metadata = read.Metadata;
            var ns = metadata.Name.Namespace;
            if (ns.NamespaceName is not ("" or
                "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd" or
                "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"))
                return new("unsafe-package");
            var licenses = metadata.Elements()
                .Where(e => e.Name.LocalName == "license").ToArray();
            if (licenses.Length > 1 || licenses.Any(e => e.Name.Namespace != ns))
                return new("unsafe-metadata");
            if (licenses.Length == 0) return new("missing-license", metadata);
            var node = licenses[0];
            var attrs = node.Attributes().Where(a => !a.IsNamespaceDeclaration).ToArray();
            if (node.HasElements || attrs.Length is < 1 or > 2 ||
                attrs.Any(a => a.Name.NamespaceName.Length != 0 ||
                    a.Name.LocalName is not ("type" or "version")) ||
                attrs.Count(a => a.Name.LocalName == "type") != 1)
                return new("unsafe-metadata");
            var version = (string?)node.Attribute("version");
            if (version is not null && !SupportedVersion(version))
                return new("unsafe-metadata");
            var type = (string?)node.Attribute("type");
            if (string.Equals(type, "file", StringComparison.OrdinalIgnoreCase))
            {
                var name = PythonTrim(node.Value);
                if (!IsSafePath(name)) return new("file-unverified", metadata);
                var file = archive.Entries.SingleOrDefault(e => e.FullName == name);
                if (file is null) return new("file-unverified", metadata);
                if (!TryRead(file, 1024 * 1024, out var bytes))
                    return new("unsafe-package");
                var declarations = ReadDeclarations(Utf8.GetString(bytes), out var remaining, out var ambiguous);
                return new(declarations.SequenceEqual(["MIT"]) && !ambiguous &&
                    PythonBlank(remaining) ? "file-declared" : "file-unverified",
                    metadata, LicenseFilePath: name);
            }
            if (!string.Equals(type, "expression", StringComparison.OrdinalIgnoreCase))
                return new("unsafe-metadata");
            Lp01SpdxExpression expression;
            try { expression = Lp01SpdxExpression.Parse(node.Value); }
            catch (SpdxLimitException) { return new("expression-limit", metadata); }
            catch (SpdxSyntaxException) { return new("invalid-expression", metadata); }
            var branch = Classify(expression, policy);
            var licenseFile = archive.Entries.SingleOrDefault(e => e.FullName == "LICENSE.txt");
            var matchedDeclaration = false;
            if (licenseFile is not null)
            {
                if (!TryRead(licenseFile, 1024 * 1024, out var bytes))
                    return new("unsafe-package");
                var ids = ReadDeclarations(Utf8.GetString(bytes), out _, out var ambiguous);
                if (ambiguous || ids.Any(id => !expression.LicenseIds.Contains(id)))
                    return new("conflicting-declarations", metadata, LicenseFilePath: "LICENSE.txt");
                matchedDeclaration = ids.Count > 0;
            }
            return new(branch, metadata, expression,
                matchedDeclaration ? "LICENSE.txt" : null);
        }
        catch (InvalidDataException) { return new("unsafe-package"); }
        catch (IOException) { return new("unsafe-package"); }
        catch (NotSupportedException) { return new("unsafe-package"); }
        catch (DecoderFallbackException) { return new("unsafe-package"); }
    }

    private static bool TryRead(ZipArchiveEntry entry, int limit, out byte[] bytes)
    {
        bytes = [];
        if (entry.Length > limit) return false;
        using var source = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int size;
        while ((size = source.Read(chunk)) > 0)
        {
            if (buffer.Length + size > limit) return false;
            buffer.Write(chunk, 0, size);
        }
        bytes = buffer.ToArray();
        return true;
    }

    internal static bool SupportedXmlEncoding(byte[] bytes)
    {
        var content = bytes.AsSpan();
        string text;
        var encoding = "utf-8";
        try
        {
            if (content.StartsWith(new byte[] { 0xff, 0xfe, 0, 0 }) ||
                content.StartsWith(new byte[] { 0, 0, 0xfe, 0xff }))
                return false;
            if (content.StartsWith(new byte[] { 0xff, 0xfe }))
            {
                encoding = "utf-16";
                text = new UnicodeEncoding(false, false, true).GetString(content[2..]);
            }
            else if (content.StartsWith(new byte[] { 0xfe, 0xff }))
            {
                encoding = "utf-16";
                text = new UnicodeEncoding(true, false, true).GetString(content[2..]);
            }
            else
            {
                var utf8 = content.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })
                    ? content[3..] : content;
                if (utf8.Contains((byte)0)) return false;
                text = Utf8.GetString(utf8);
            }
        }
        catch (DecoderFallbackException) { return false; }
        var declaration = XmlEncoding.Match(text);
        return !declaration.Success ||
            string.Equals(declaration.Groups[2].Value, encoding, StringComparison.OrdinalIgnoreCase);
    }

    private static string Classify(Lp01SpdxExpression expression, Policy policy)
    {
        var terms = expression.Terms;
        if (terms.Any(id => id.StartsWith("WITH:", StringComparison.Ordinal)))
            return "exception-unreviewed";
        if (terms.Any(id => id.StartsWith("LicenseRef-", StringComparison.Ordinal) ||
                            id.StartsWith("DocumentRef-", StringComparison.Ordinal)))
            return "custom-license";
        if (terms.Any(id => !policy.Licenses.ContainsKey(id))) return "unknown-license";
        if (terms.Any(id => policy.Licenses[id].Deprecated || id.EndsWith('+')))
            return "deprecated-license";
        var options = expression.Alternatives;
        bool NonOsi(HashSet<string> option) => option.Any(id => !policy.Licenses[id].Osi);
        if (terms.Any(id => !policy.Licenses[id].Osi))
            return options.All(NonOsi) ? "non-osi" : "mixed-alternatives";
        if (options.All(option => option.IsSubsetOf(policy.Allowed)))
            return "approved-non-copyleft";
        bool Copyleft(HashSet<string> option) => option.Overlaps(policy.Strong) ||
            option.Overlaps(policy.Weak);
        if (options.All(Copyleft))
            return options.All(option => option.Overlaps(policy.Weak) &&
                !option.Overlaps(policy.Strong)) ? "weak-copyleft" : "copyleft";
        if (options.Any(Copyleft) && options.Any(option => option.IsSubsetOf(policy.Allowed)))
            return "mixed-alternatives";
        return "unclassified-osi";
    }

    private static List<string> ReadDeclarations(string text, out string remaining, out bool ambiguous)
    {
        var ids = new List<string>();
        var lines = Regex.Split(text, @"\r\n|\r|\n");
        var rest = new StringBuilder();
        ambiguous = false;
        foreach (var line in lines)
        {
            var match = Declaration.Match(line);
            if (match.Success) ids.Add(match.Groups[1].Value);
            else if (line.Contains("spdx-license-identifier", StringComparison.OrdinalIgnoreCase))
                ambiguous = true;
            else rest.Append(line);
        }
        remaining = rest.ToString();
        return ids;
    }

    private static bool SupportedVersion(string version)
    {
        var parts = version.Split('.');
        if (parts.Length is < 2 or > 4 ||
            parts.Any(part => part.Length == 0 || part.Any(c => c is < '0' or > '9')))
            return false;
        if (parts[0].All(c => c == '0')) return true;
        return parts[0].TrimStart('0') == "1" &&
            parts.Skip(1).All(part => part.All(c => c == '0'));
    }

    private static bool PythonWhitespace(char value) =>
        char.IsWhiteSpace(value) || value is >= '\u001c' and <= '\u001f';

    private static bool PythonBlank(string value) => value.All(PythonWhitespace);

    private static string PythonTrim(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && PythonWhitespace(value[start])) start++;
        while (end > start && PythonWhitespace(value[end - 1])) end--;
        return value[start..end];
    }

    private static bool IsSafePath(string? value) =>
        SafeText(value) && SafePath.IsMatch(value!) &&
        !value!.Split('/').Any(part => part is "." or "..");

    private static bool SafeText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var count = 0;
        for (var index = 0; index < value.Length;)
        {
            if (Rune.DecodeFromUtf16(value.AsSpan(index), out var rune, out var consumed) !=
                    OperationStatus.Done ||
                ++count > 256 || rune.Value < 32 || rune.Value is >= 127 and <= 159)
                return false;
            index += consumed;
        }
        return value.IsNormalized(NormalizationForm.FormC);
    }

    private static string CanonicalOwner(JsonElement record)
    {
        var result = new StringBuilder();
        WriteJson(record, result, 0);
        return result.Append('\n').ToString();
    }

    private static void WriteJson(JsonElement value, StringBuilder target, int level)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = value.EnumerateObject().ToArray();
                target.Append('{');
                for (var index = 0; index < properties.Length; index++)
                {
                    target.Append('\n', 1).Append(' ', (level + 1) * 2);
                    WriteJsonString(properties[index].Name, target);
                    target.Append(": ");
                    WriteJson(properties[index].Value, target, level + 1);
                    if (index != properties.Length - 1) target.Append(',');
                }
                if (properties.Length > 0)
                    target.Append('\n').Append(' ', level * 2);
                target.Append('}');
                break;
            case JsonValueKind.Array:
                var elements = value.EnumerateArray().ToArray();
                target.Append('[');
                for (var index = 0; index < elements.Length; index++)
                {
                    target.Append('\n').Append(' ', (level + 1) * 2);
                    WriteJson(elements[index], target, level + 1);
                    if (index != elements.Length - 1) target.Append(',');
                }
                if (elements.Length > 0)
                    target.Append('\n').Append(' ', level * 2);
                target.Append(']');
                break;
            case JsonValueKind.String:
                WriteJsonString(value.GetString()!, target);
                break;
            case JsonValueKind.Number:
                target.Append(value.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.True:
                target.Append("true");
                break;
            case JsonValueKind.False:
                target.Append("false");
                break;
            default:
                throw new InvalidDataException("Unexpected field in canonical LP-01 owner record.");
        }
    }

    private static void WriteJsonString(string value, StringBuilder target)
    {
        target.Append('"');
        foreach (var character in value)
        {
            if (character == '"') target.Append("\\\"");
            else if (character == '\\') target.Append("\\\\");
            else if (character >= 128)
                target.Append("\\u").Append(((int)character).ToString("x4",
                    System.Globalization.CultureInfo.InvariantCulture));
            else target.Append(character);
        }
        target.Append('"');
    }

    private static bool SafeJsonText(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) return false;
        try { return SafeText(value.GetString()); }
        catch (InvalidOperationException) { return false; }
    }

    private static string? Text(JsonElement element, string key)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        try { return value.GetString(); }
        catch (InvalidOperationException) { return null; }
    }

    private static bool Fields(JsonElement element, string[] fields, bool ordered)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        try
        {
            var names = element.EnumerateObject().Select(p => p.Name).ToArray();
            return names.Length == fields.Length &&
                (ordered ? names.SequenceEqual(fields, StringComparer.Ordinal) :
                    names.ToHashSet(StringComparer.Ordinal).SetEquals(fields));
        }
        catch (InvalidOperationException) { return false; }
    }

    private static string InspectOwner(ResolverCase input, CheckedInput package,
        CheckedInput policy, PackageState state)
    {
        if (!input.CheckedInputs.TryGetValue("owner-record", out var owner))
            return "approved-non-copyleft";
        if (input.CheckedInputs.ContainsKey("owner-record-conflict"))
            return "owner-record-conflict";
        var bytes = owner.Content.Span;
        if (bytes.Length > 8192 || bytes.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
            return "owner-record-invalid";
        JsonDocument document;
        try { document = JsonDocument.Parse(bytes.ToArray()); }
        catch (JsonException) { return "owner-record-invalid"; }
        using (document)
        {
            var record = document.RootElement;
            if (!Fields(record, OwnerFields, ordered: true) ||
                record.GetProperty("schema_version").ValueKind != JsonValueKind.Number ||
                !record.GetProperty("schema_version").TryGetInt32(out var schema) || schema != 1 ||
                record.GetProperty("caller_accepts_prevalidated_authority").ValueKind is not
                    (JsonValueKind.True or JsonValueKind.False) ||
                !OwnerFields.Where(key => key is not ("schema_version" or
                    "caller_accepts_prevalidated_authority" or "grant_bindings" or
                    "prevalidation_references")).All(key => SafeText(Text(record, key))) ||
                !record.TryGetProperty("prevalidation_references", out var refs) ||
                refs.ValueKind != JsonValueKind.Array || refs.GetArrayLength() > 32 ||
                refs.EnumerateArray().Any(item => !SafeJsonText(item)) ||
                !Commit.IsMatch(Text(record, "source_commit")!) ||
                !Text(record, "release_ref")!.StartsWith("refs/tags/", StringComparison.Ordinal) ||
                Text(record, "release_ref")!.Length <= "refs/tags/".Length ||
                !new[] { "package_sha256", "policy_sha256", "spdx_sha256" }
                    .All(key => Hex.IsMatch(Text(record, key)!)) ||
                !record.TryGetProperty("grant_bindings", out var bindings) ||
                bindings.ValueKind != JsonValueKind.Array ||
                bindings.GetArrayLength() is < 1 or > 32)
                return "owner-record-invalid";
            foreach (var binding in bindings.EnumerateArray())
            {
                if (!Fields(binding, BindingFields, ordered: true) ||
                    !new[] { "license_id", "role", "sha256" }.All(key => SafeText(Text(binding, key))) ||
                    !IsSafePath(Text(binding, "path")) ||
                    binding.GetProperty("caller_accepts_complete_grant").ValueKind is not
                        (JsonValueKind.True or JsonValueKind.False) ||
                    !Hex.IsMatch(Text(binding, "sha256")!))
                    return "owner-record-invalid";
            }
            if (!bytes.SequenceEqual(Utf8.GetBytes(CanonicalOwner(record))))
                return "owner-record-invalid";
            if (record.GetProperty("caller_accepts_prevalidated_authority").ValueKind != JsonValueKind.True)
                return "owner-acceptance-missing";
            var metadata = state.Metadata!;
            var ns = metadata.Name.Namespace;
            var repositories = metadata.Elements(ns + "repository").ToArray();
            if (repositories.Length != 1 || repositories[0].HasElements ||
                metadata.Element(ns + "id") is not { HasElements: false } ||
                metadata.Element(ns + "version") is not { HasElements: false })
                return "owner-binding-conflict";
            Lp01SpdxExpression ownerExpression;
            try { ownerExpression = Lp01SpdxExpression.Parse(Text(record, "spdx_grant_expression")!); }
            catch (SpdxLimitException) { return "owner-binding-conflict"; }
            catch (SpdxSyntaxException) { return "owner-binding-conflict"; }
            if (Text(record, "package_sha256") != package.Identity.Sha256 ||
                Text(record, "package_id") != metadata.Element(ns + "id")?.Value ||
                Text(record, "repository_url") != (string?)repositories[0].Attribute("url") ||
                Text(record, "source_commit") != (string?)repositories[0].Attribute("commit") ||
                Text(record, "release_version") != metadata.Element(ns + "version")?.Value ||
                Text(record, "spdx_grant_expression") != ownerExpression.Canonical ||
                ownerExpression.Canonical != state.Expression!.Canonical ||
                Text(record, "policy_version") != "1.0.0" ||
                Text(record, "policy_sha256") != policy.Identity.Sha256 ||
                Text(record, "spdx_version") != "3.27.0" ||
                Text(record, "spdx_sha256") != CorpusDigest)
                return "owner-binding-conflict";
            var expected = state.Expression.LicenseIds.Order(StringComparer.Ordinal).ToArray();
            var actual = bindings.EnumerateArray().Select(b => Text(b, "license_id")!).ToArray();
            if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
                return actual.ToHashSet(StringComparer.Ordinal).IsProperSubsetOf(expected)
                    ? "grant-missing" : "grant-binding-conflict";
            var requiredRoles = expected.Select(id => "grant-" + id.ToLowerInvariant())
                .ToHashSet(StringComparer.Ordinal);
            if (input.Inputs.Any(i => i.Role.StartsWith("grant-", StringComparison.Ordinal) &&
                !requiredRoles.Contains(i.Role)))
                return "owner-record-invalid";
            foreach (var binding in bindings.EnumerateArray())
            {
                var licenseId = Text(binding, "license_id")!;
                var role = "grant-" + licenseId.ToLowerInvariant();
                if (Text(binding, "role") != role)
                    return "grant-binding-conflict";
                if (!input.CheckedInputs.TryGetValue(role, out var grant))
                    return "grant-missing";
                if (grant.Identity.Path != Text(binding, "path") ||
                    grant.Identity.Sha256 != Text(binding, "sha256") ||
                    Catalog.Digest(grant.Content.Span) != Text(binding, "sha256"))
                    return "grant-binding-conflict";
                if (grant.Content.Length > 16384) return "grant-limit";
                string text;
                try { text = Utf8.GetString(grant.Content.Span); }
                catch (DecoderFallbackException) { return "grant-incomplete"; }
                if (text.Length == 0 || text.Contains('\0') ||
                    grant.Content.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
                    return "grant-incomplete";
                var declarations = ReadDeclarations(text, out var remaining, out var ambiguous);
                if (ambiguous) return "grant-incomplete";
                if (declarations.Any(id => id != licenseId)) return "grant-contradiction";
                if (PythonBlank(remaining) ||
                    binding.GetProperty("caller_accepts_complete_grant").ValueKind != JsonValueKind.True)
                    return "grant-incomplete";
            }
            return "owner-evidence-accepted";
        }
    }

    private static CaseResult Result(ResolverCase input, string branch, string? licenseFilePath = null)
    {
        var unavailable = branch is "unsafe-package" or "missing-package" or "missing-policy" or
            "missing-corpus" or "invalid-policy";
        var isOwner = branch.StartsWith("owner-", StringComparison.Ordinal) ||
            branch.StartsWith("grant-", StringComparison.Ordinal);
        var references = new List<string>();
        if (!unavailable)
        {
            references.Add(branch == "missing-license" ? "package:nuspec/metadata" :
                "package:nuspec/license");
            if (isOwner) references.Add("package:nuspec/repository");
            if (branch is "file-declared" or "conflicting-declarations")
                references.Add("package:" + licenseFilePath);
            references.Add("policy:lp01-policy-1.0.0");
            references.Add("corpus:spdx-licenses-3.27.0");
            if (isOwner)
            {
                if (input.CheckedInputs.ContainsKey("owner-record"))
                    references.Add("owner-record:document");
                references.AddRange(input.Inputs.Where(i => i.Role.StartsWith("grant-", StringComparison.Ordinal))
                    .Select(i => i.Role + ":document"));
            }
            else if (licenseFilePath is not null && branch == "approved-non-copyleft")
                references.Add("package:" + licenseFilePath);
        }
        var deterministic = branch is "owner-evidence-accepted" or "copyleft" or
            "weak-copyleft" or "non-osi";
        var finding = branch switch
        {
            "owner-evidence-accepted" =>
                "The checked package SPDX expression is OSI-approved and non-copyleft under pinned policy; every offered license ID has a checked grant binding matching the prevalidated owner/repository record. Caller-asserted authority, grant sufficiency and release scope were accepted at the input boundary, NOT cryptographically established or legally interpreted by this resolver.",
            "copyleft" or "weak-copyleft" or "non-osi" =>
                $"The authoritative checked package declaration offers no OSI-approved non-copyleft alternative ({branch}); this is a package-scoped negative, not a repository-wide verdict.",
            _ => null
        };
        var (missing, required, next) = branch switch
        {
            "owner-evidence-accepted" or "copyleft" or "weak-copyleft" or "non-osi" =>
                ((string?)null, (string?)null, (string?)null),
            "approved-non-copyleft" => (
                "An OSI-approved, non-copyleft package declaration does not establish an owner-authorized repository license grant or correspondence to the checked release/source scope.",
                "owner-license-grant-and-release-scope", "package-owner"),
            "file-declared" => (
                "The packaged SPDX-License-Identifier classifies MIT but is not license grant text; owner authorization and repository/release scope are also missing.",
                "complete-owner-license-grant-and-release-scope", "package-owner"),
            "owner-acceptance-missing" => ("Caller did not explicitly accept the owner-authority prevalidation trust boundary.",
                "explicit-caller-authority-acceptance", "input-owner"),
            "owner-record-invalid" => ("The owner record is malformed, noncanonical, over 8192 bytes or has unknown/missing schema fields.",
                "canonical-versioned-owner-record", "input-owner"),
            "owner-record-conflict" => ("Multiple owner records assert conflicting identities for the checked package.",
                "single-unambiguous-owner-record", "input-owner"),
            "owner-binding-conflict" => ("Owner-record package, repository, source/release, expression or pinned policy identity conflicts with checked inputs.",
                "matching-owner-package-and-policy-bindings", "package-owner"),
            "grant-binding-conflict" => ("Owner-record grant path or digest conflicts with the independently checked grant input.",
                "matching-checked-grant-artifact", "package-owner"),
            "grant-missing" => ("The checked complete grant artifact was not supplied.",
                "checked-complete-license-grant", "package-owner"),
            "grant-incomplete" => ("The checked license artifact is tag-only or the caller has not prevalidated its complete grant text.",
                "prevalidated-complete-license-grant-text", "package-owner"),
            "grant-limit" => ("The checked license grant exceeds the 16384-byte v1 inspection limit.",
                "bounded-complete-license-grant", "package-owner"),
            "grant-contradiction" => ("The authoritative package declaration and prevalidated grant evidence contradict each other.",
                "reconciled-owner-license-grant", "package-owner"),
            "mixed-alternatives" => ("An OR alternative is not established as OSI-approved and non-copyleft.",
                "binding-license-choice", "package-owner"),
            "unclassified-osi" => ("SPDX marks the identifier OSI-approved but this policy does not classify its copyleft obligations.",
                "reviewed-copyleft-classification", "policy-owner"),
            "custom-license" => ("A custom LicenseRef has no pinned OSI/copy-left classification.",
                "reviewed-custom-license", "policy-owner"),
            "exception-unreviewed" => ("WITH exception semantics have no pinned policy classification.",
                "reviewed-exception-policy", "policy-owner"),
            "unknown-license" => ("The identifier is not an exact SPDX 3.27.0 license ID.",
                "corrected-spdx-expression", "package-owner"),
            "deprecated-license" => ("Deprecated SPDX spelling cannot be silently normalized.",
                "current-spdx-expression-and-owner-confirmation", "package-owner"),
            "invalid-expression" => ("The expression is not valid under the frozen SPDX expression grammar.",
                "corrected-spdx-expression", "package-owner"),
            "file-unverified" => ("A license file declaration alone does not establish a complete, authoritative package grant or repository/release scope.",
                "owner-verified-license-grant-and-release-scope", "package-owner"),
            "expression-limit" => ("The SPDX expression exceeds the pinned parsing resource limits and cannot be classified safely.",
                "bounded-owner-confirmed-license-expression", "package-owner"),
            "conflicting-declarations" => ("The nuspec expression and packaged explicit SPDX identifier conflict.",
                "reconciled-license-grant", "package-owner"),
            "missing-license" => ("No authoritative license expression or supported file declaration was supplied.",
                "license-grant-and-owner-confirmation", "package-owner"),
            "unsafe-metadata" => ("License metadata is malformed, unsupported or ambiguous.",
                "corrected-nuspec", "package-owner"),
            "unsafe-package" => ("Package or nuspec cannot be safely and unambiguously inspected.",
                "valid-unambiguous-package", "package-owner"),
            "missing-package" => ("Checked package bytes were not supplied.", "package", "input-owner"),
            "missing-policy" => ("Pinned LP-01 policy is missing.", "pinned-license-policy", "policy-owner"),
            "missing-corpus" => ("Pinned SPDX license catalog is missing.", "pinned-spdx-catalog", "policy-owner"),
            "invalid-policy" => ("Pinned LP-01 policy and SPDX catalog are inconsistent.",
                "consistent-pinned-license-policy", "policy-owner"),
            _ => throw new InvalidDataException($"Unsupported LP-01 decision branch: {branch}")
        };
        var contract = branch switch
        {
            "approved-non-copyleft" => PackageContract,
            "file-declared" =>
                $"Checked {licenseFilePath} contains only SPDX-License-Identifier: MIT; MIT is OSI-approved and allowlisted non-copyleft, but the file supplies no actual license grant text.",
            "owner-evidence-accepted" => AcceptedContract,
            _ when isOwner => OwnerContract,
            _ => null
        };
        return new("LP-01", "lp01-license-policy", "1.0.0", branch,
            deterministic ? "deterministic" : "escalated", unavailable ? "unknown" : "applicable",
            input.Inputs, references, contract, finding,
            deterministic ? null : missing, deterministic ? null : required,
            deterministic ? null : next);
    }
}
