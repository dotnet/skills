using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BlazorPackageResolver;

public sealed class Lp03DependencyLicensesResolver : IDeterministicResolver
{
    private const string Contract =
        "Exact root bytes and explicit target-framework union only. Accepted acquisition observations " +
        "supply resolved closure and package inspection; caller-prevalidated reviews supply complete " +
        "grant applicability to each entire dependency version. Hashes prove bindings, not those " +
        "observations' truth or reviewer authority. No legal approval or overall readiness conclusion.";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Regex IdPattern = new(@"\A[a-z0-9][a-z0-9.-]{0,63}\z", RegexOptions.CultureInvariant);
    private static readonly Regex VersionPattern = new(
        @"\A(?:0|[1-9][0-9]{0,4})(?:\.(?:0|[1-9][0-9]{0,4})){2}\z", RegexOptions.CultureInvariant);
    private static readonly Regex DigestPattern = new(@"\A[a-f0-9]{64}\z", RegexOptions.CultureInvariant);
    private static readonly Regex DependencyRole = new(@"\Adependency-[0-9]{3}\z", RegexOptions.CultureInvariant);
    private static readonly Regex GrantRole = new(@"\Agrant-[0-9]{3}\z", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        MaxDepth = 10
    };
    private sealed record Identity(string Id, string Version, string Sha256);
    private sealed record Assessment(int SchemaVersion, Identity Root, string[] TargetFrameworks);
    private sealed record TargetCount(string Tfm, int NodeCount, int EdgeCount);
    private sealed record Completeness(string Profile, TargetCount[] Targets);
    private sealed record Grant(string Role, string Path, string Sha256, string PackageEntry);
    private sealed record Dependency(string Key, Identity Identity, string Role, string? DeclaredExpression, Grant? Grant);
    private sealed record Capture(int SchemaVersion, string Producer, Identity Root, string? GraphSha256,
        string[] TargetFrameworks, string Result, Completeness CompletenessBasis,
        Dependency[] Packages, string CaptureReference);
    private sealed record Node(string Key, string[] Dependencies);
    private sealed record Target(string Tfm, string[] Direct, Node[] Nodes);
    private sealed record Graph(int SchemaVersion, Identity Root, Target[] Targets, string[] Unresolved);
    private sealed record Review(string Key, string PackageSha256, string GrantSha256, string LicenseId,
        string Scope, string Basis, string Disposition, string ReviewerReference,
        string AuthorityReference, string ReviewReference);
    public string RowId => "LP-03";
    public string ResolverId => "lp03-dependency-licenses";
    public string Version => "1.0.0";
    internal static bool ValidId(string value) => IdPattern.IsMatch(value);
    internal static bool ValidVersion(string value) => VersionPattern.IsMatch(value);

    public CaseResult Resolve(ResolverCase input)
    {
        var checkedInputs = input.CheckedInputs;
        if (!checkedInputs.TryGetValue("package", out var package)) return Result(input, "missing-package");
        if (!checkedInputs.ContainsKey("assessment")) return Result(input, "scope-missing");
        Assessment scope;
        Capture? capture;
        Graph? graph;
        ReviewDocument? reviews;
        try
        {
            scope = Read<Assessment>(checkedInputs["assessment"].Content);
            capture = Optional<Capture>(checkedInputs, "acquisition-receipt");
            graph = Optional<Graph>(checkedInputs, "dependency-graph");
            reviews = Optional<ReviewDocument>(checkedInputs, "license-reviews");
            Validate(scope, capture, graph, reviews, input);
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or DecoderFallbackException)
        {
            return Result(input, "input-invalid");
        }
        if (!checkedInputs.TryGetValue("policy", out var policyBytes) ||
            !checkedInputs.TryGetValue("corpus", out var corpusBytes) ||
            Lp01LicensePolicyResolver.ReadPolicy(policyBytes.Content.Span, corpusBytes.Content.Span) is not { } policy)
            return Result(input, "policy-unavailable");
        if (scope.Root.Sha256 != package.Identity.Sha256) return Result(input, "binding-conflict");
        var root = Lp03PackageEvidence.Read(package.Content, null);
        if (root is null) return Result(input, "input-invalid");
        if (root.Id != scope.Root.Id || root.Version != scope.Root.Version) return Result(input, "binding-conflict");
        if (capture is null) return Result(input, "evidence-missing");
        if (capture.Root != scope.Root) return Result(input, "binding-conflict");
        if (capture.Result != "captured") return Result(input, "closure-unresolved");
        if (graph is null) return Result(input, "evidence-missing");
        if (graph.Root != scope.Root ||
            capture.GraphSha256 != checkedInputs["dependency-graph"].Identity.Sha256)
            return Result(input, "binding-conflict");
        var requested = scope.TargetFrameworks.Order(StringComparer.Ordinal).ToArray();
        if (!capture.TargetFrameworks.Order(StringComparer.Ordinal).SequenceEqual(requested) ||
            !graph.Targets.Select(t => t.Tfm).Order(StringComparer.Ordinal).SequenceEqual(requested) ||
            graph.Unresolved.Length != 0)
            return Result(input, "closure-unresolved");
        var union = new HashSet<string>(StringComparer.Ordinal);
        var counts = new List<TargetCount>();
        foreach (var target in graph.Targets)
        {
            var nodes = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (target.Nodes.Any(node => !nodes.TryAdd(node.Key, node.Dependencies)) ||
                target.Direct.Distinct(StringComparer.Ordinal).Count() != target.Direct.Length ||
                nodes.Values.Any(edges => edges.Distinct(StringComparer.Ordinal).Count() != edges.Length))
                return Result(input, "closure-unresolved");
            var edges = target.Direct.Concat(nodes.Values.SelectMany(e => e)).ToArray();
            if (edges.Length > 128 || edges.Any(edge => !nodes.ContainsKey(edge)) ||
                nodes.Keys.Select(key => key.Split('/')[0]).Distinct(StringComparer.Ordinal).Count() != nodes.Count)
                return Result(input, "closure-unresolved");
            var reached = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>(target.Direct);
            while (pending.TryPop(out var key))
                if (reached.Add(key))
                    foreach (var edge in nodes[key]) pending.Push(edge);
            if (!reached.SetEquals(nodes.Keys)) return Result(input, "closure-unresolved");
            union.UnionWith(nodes.Keys);
            counts.Add(new(target.Tfm, nodes.Count, edges.Length));
        }
        if (union.Count > 64 || !counts.OrderBy(c => c.Tfm, StringComparer.Ordinal).SequenceEqual(
            capture.CompletenessBasis.Targets.OrderBy(c => c.Tfm, StringComparer.Ordinal)))
            return Result(input, "closure-unresolved");
        var inventory = new Dictionary<string, Dependency>(StringComparer.Ordinal);
        if (capture.Packages.Any(item => !inventory.TryAdd(item.Key, item)) ||
            !union.SetEquals(inventory.Keys))
            return Result(input, "closure-unresolved");
        var reviewMap = new Dictionary<string, Review>(StringComparer.Ordinal);
        if (reviews is not null && reviews.Reviews.Any(review =>
            !union.Contains(review.Key) || !reviewMap.TryAdd(review.Key, review)))
            return Result(input, "binding-conflict");
        var used = new HashSet<string>(["package", "assessment", "dependency-graph",
            "acquisition-receipt", "policy", "corpus"], StringComparer.Ordinal);
        if (reviews is not null) used.Add("license-reviews");
        bool missingPackage = false, unresolved = false, owner = false, incompatible = false;
        var index = 0;
        foreach (var key in union.Order(StringComparer.Ordinal))
        {
            var item = inventory[key];
            var suffix = (index++).ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
            if (key != $"{item.Identity.Id}/{item.Identity.Version}" || item.Role != $"dependency-{suffix}")
                return Result(input, "binding-conflict");
            used.Add(item.Role);
            Lp03PackageEvidence? actual = null;
            if (!checkedInputs.TryGetValue(item.Role, out var dependency)) missingPackage = true;
            else
            {
                if (dependency.Identity.Sha256 != item.Identity.Sha256) return Result(input, "binding-conflict");
                actual = Lp03PackageEvidence.Read(dependency.Content, item.Grant?.PackageEntry);
                if (actual is null) return Result(input, "input-invalid");
                if (actual.Id != item.Identity.Id || actual.Version != item.Identity.Version ||
                    actual.Expression != item.DeclaredExpression)
                    return Result(input, "binding-conflict");
            }
            var grant = item.Grant;
            reviewMap.TryGetValue(key, out var review);
            if (grant is not null)
            {
                if (grant.Role != $"grant-{suffix}") return Result(input, "binding-conflict");
                used.Add(grant.Role);
                if (checkedInputs.TryGetValue(grant.Role, out var retained) &&
                    (retained.Identity.Path != grant.Path || retained.Identity.Sha256 != grant.Sha256) ||
                    actual is not null && actual.GrantSha256 != grant.Sha256)
                    return Result(input, "binding-conflict");
            }
            if (review is not null && (review.PackageSha256 != item.Identity.Sha256 ||
                grant is not null && review.GrantSha256 != grant.Sha256))
                return Result(input, "binding-conflict");
            if (grant is null || !checkedInputs.ContainsKey(grant.Role) || review is null)
            {
                unresolved = true;
                continue;
            }
            var expression = item.DeclaredExpression;
            if (expression != review.LicenseId) return Result(input, "binding-conflict");
            if (review.Disposition == "owner-grant-required")
            {
                if (expression == "LicenseRef-owner-grant" && review.Basis == "unpublished-owner-grant") owner = true;
                else unresolved = true;
                continue;
            }
            if (review.Disposition != "applicable-single-license" || review.Basis != "published-package-grant" ||
                expression is null || !policy.Licenses.TryGetValue(expression, out var license) || license.Deprecated)
            {
                unresolved = true;
                continue;
            }
            if (policy.Allowed.Contains(expression)) continue;
            if (policy.Strong.Contains(expression) || policy.Weak.Contains(expression) || !license.Osi) incompatible = true;
            else unresolved = true;
        }
        if (checkedInputs.Keys.Any(role => !used.Contains(role))) return Result(input, "binding-conflict");
        if (missingPackage) return Result(input, "dependency-bytes-missing");
        if (unresolved) return Result(input, "license-evidence-unresolved");
        if (owner) return Result(input, "owner-evidence-required");
        return Result(input, incompatible ? "incompatible-dependency-license" : "dependencies-policy-satisfied");
    }

    private sealed record ReviewDocument(int SchemaVersion, Review[] Reviews);
    private static T? Optional<T>(IReadOnlyDictionary<string, CheckedInput> inputs, string role) where T : class =>
        inputs.TryGetValue(role, out var value) ? Read<T>(value.Content) : null;

    private static T Read<T>(ReadOnlyMemory<byte> data) where T : class
    {
        Require(data.Length <= 65536 && !data.Span.StartsWith(new byte[] { 239, 187, 191 }), "JSON byte limit/BOM");
        using var document = JsonDocument.Parse(Utf8.GetString(data.Span), new() { MaxDepth = 10 });
        try { Bound(document.RootElement, 0); }
        catch (InvalidOperationException error)
        {
            // JsonDocument defers malformed escaped UTF-16 rejection until string access.
            throw new InvalidDataException("Invalid normalized JSON string.", error);
        }
        return document.RootElement.Deserialize<T>(JsonOptions) ?? throw new InvalidDataException("Null document.");
    }

    private static void Bound(JsonElement value, int depth)
    {
        Require(depth <= 8, "JSON depth");
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    Text(property.Name);
                    Require(names.Add(property.Name), "Duplicate JSON field");
                    Bound(property.Value, depth + 1);
                }
                break;
            case JsonValueKind.Array:
                foreach (var child in value.EnumerateArray()) Bound(child, depth + 1);
                break;
            case JsonValueKind.String: Text(value.GetString()!, 1024); break;
            case JsonValueKind.Null: break;
            case JsonValueKind.Number:
                Require(value.GetRawText().All(ch => ch is '-' or >= '0' and <= '9'), "Noninteger scalar");
                break;
            default: throw new InvalidDataException("Unsupported JSON scalar.");
        }
    }

    private static void Validate(Assessment scope, Capture? capture, Graph? graph,
        ReviewDocument? reviews, ResolverCase input)
    {
        Require(scope.SchemaVersion == 1, "Scope schema");
        ValidateIdentity(scope.Root);
        List(scope.TargetFrameworks, 4);
        Require(scope.TargetFrameworks.Length > 0 &&
            scope.TargetFrameworks.Distinct(StringComparer.Ordinal).Count() == scope.TargetFrameworks.Length, "Scope targets");
        foreach (var tfm in scope.TargetFrameworks) Require(ValidId(tfm), "Target");
        if (capture is not null)
        {
            Require(capture.SchemaVersion == 1 && capture.Producer == "lp03-resolved-target-capture/1.0.0", "Capture producer");
            ValidateIdentity(capture.Root);
            List(capture.TargetFrameworks, 4);
            foreach (var tfm in capture.TargetFrameworks) Require(ValidId(tfm), "Target");
            Text(capture.CaptureReference);
            Require(capture.Result is "captured" or "unavailable", "Capture result");
            if (capture.GraphSha256 is not null) Sha(capture.GraphSha256);
            Require(capture.CompletenessBasis is not null &&
                capture.CompletenessBasis.Profile == "resolved-target-graph/1", "Completeness profile");
            List(capture.CompletenessBasis.Targets, 4);
            foreach (var count in capture.CompletenessBasis.Targets)
                Require(ValidId(count.Tfm) && count.NodeCount is >= 0 and <= 64 &&
                    count.EdgeCount is >= 0 and <= 128, "Completeness counts");
            List(capture.Packages, 64);
            Require(capture.Result != "unavailable" || capture.GraphSha256 is null &&
                capture.Packages.Length == 0 && capture.CompletenessBasis.Targets.Length == 0, "Failed capture shape");
            foreach (var item in capture.Packages)
            {
                Key(item.Key);
                ValidateIdentity(item.Identity);
                Require(DependencyRole.IsMatch(item.Role), "Dependency role");
                if (item.DeclaredExpression is not null) Text(item.DeclaredExpression);
                if (item.Grant is { } grant)
                {
                    Require(GrantRole.IsMatch(grant.Role) && Lp02PublicSourceResolver.SafePath(grant.Path) &&
                        Lp02PublicSourceResolver.SafePath(grant.PackageEntry), "Grant role/path");
                    Sha(grant.Sha256);
                }
            }
        }
        if (graph is not null)
        {
            Require(graph.SchemaVersion == 1, "Graph schema");
            ValidateIdentity(graph.Root);
            List(graph.Targets, 4);
            List(graph.Unresolved, 64);
            foreach (var diagnostic in graph.Unresolved) Text(diagnostic);
            foreach (var target in graph.Targets)
            {
                Require(ValidId(target.Tfm), "Target");
                List(target.Direct, 64);
                List(target.Nodes, 64);
                foreach (var key in target.Direct) Key(key);
                foreach (var node in target.Nodes)
                {
                    Key(node.Key);
                    List(node.Dependencies, 64);
                    foreach (var key in node.Dependencies) Key(key);
                }
            }
        }
        if (reviews is not null)
        {
            Require(reviews.SchemaVersion == 1, "Review schema");
            List(reviews.Reviews, 64);
            foreach (var review in reviews.Reviews)
            {
                Key(review.Key);
                Sha(review.PackageSha256);
                Sha(review.GrantSha256);
                Text(review.LicenseId);
                Text(review.ReviewerReference);
                Text(review.AuthorityReference);
                Text(review.ReviewReference);
                Require(review.Scope == "entire-dependency-version" &&
                    review.Basis is "published-package-grant" or "unpublished-owner-grant" &&
                    review.Disposition is "applicable-single-license" or "owner-grant-required" or "unresolved", "Review profile");
            }
        }
        long total = 0;
        foreach (var (role, value) in input.CheckedInputs)
        {
            Require(Lp02PublicSourceResolver.SafePath(value.Identity.Path) &&
                (role is "package" or "assessment" or "dependency-graph" or "acquisition-receipt" or
                    "license-reviews" or "policy" or "corpus" || DependencyRole.IsMatch(role) || GrantRole.IsMatch(role)), "Input role/path");
            total += value.Content.Length;
            if (GrantRole.IsMatch(role))
            {
                Require(value.Content.Length is > 0 and <= 16384 &&
                    !value.Content.Span.StartsWith(new byte[] { 239, 187, 191 }) &&
                    !Lp01LicensePolicyResolver.PythonBlank(Utf8.GetString(value.Content.Span)), "Grant bytes");
            }
            if (role == "package" || DependencyRole.IsMatch(role))
                Require(value.Content.Length is > 0 and <= 8 * 1024 * 1024, "Archive bytes");
        }
        Require(total <= 64 * 1024 * 1024, "Aggregate bytes");
    }

    private static void ValidateIdentity(Identity value)
    {
        Require(value is not null && ValidId(value.Id) && ValidVersion(value.Version), "Identity");
        Sha(value.Sha256);
    }
    private static void Key(string value)
    {
        Text(value, 128);
        var parts = value.Split('/');
        Require(parts.Length == 2 && ValidId(parts[0]) && ValidVersion(parts[1]), "Package key");
    }
    private static void List<T>(T[] values, int maximum) =>
        Require(values is not null && values.Length <= maximum && values.All(v => v is not null), "Array");
    private static void Text(string value, int maximum = 256) =>
        Require(value is { Length: > 0 } && value.Length <= maximum &&
            value.Any(ch => ch != ' ') && value.All(ch => ch is >= ' ' and <= '~'), "Text");
    private static void Sha(string value) => Require(value is not null && DigestPattern.IsMatch(value), "SHA-256");
    private static void Require([DoesNotReturnIf(false)] bool condition, string rule)
    {
        if (!condition) throw new InvalidDataException($"Unsupported LP-03 evidence: {rule}.");
    }

    private CaseResult Result(ResolverCase input, string branch)
    {
        var (missing, required, next) = branch switch
        {
            "missing-package" => ("The exact root package is missing.", "package", "package-acquisition"),
            "scope-missing" => ("The exact root and target-framework assessment scope is missing.", "assessment", "assessment-scope"),
            "evidence-missing" => ("A checked resolved graph or acquisition receipt is missing.", "dependency-closure", "dependency-acquisition"),
            "input-invalid" => ("The checked evidence is outside the bounded unambiguous receipt profile.", "supported-evidence", "evidence-repair"),
            "policy-unavailable" => ("The pinned policy and SPDX catalog are missing or inconsistent.", "pinned-policy-and-corpus", "policy-acquisition"),
            "binding-conflict" => ("Root, graph, dependency, grant or review bindings disagree.", "consistent-checked-bindings", "evidence-repair"),
            "closure-unresolved" => ("Acquisition or the exact resolved target union and edge closure is incomplete or conflicting.", "complete-resolved-target-capture", "dependency-acquisition"),
            "dependency-bytes-missing" => ("An exact inventoried dependency archive is unavailable.", "exact-dependency-bytes", "dependency-acquisition"),
            "license-evidence-unresolved" => ("Applicable retained grants and supported single-license reviews are incomplete or ambiguous.", "applicable-license-evidence", "license-investigation"),
            "owner-evidence-required" => ("A checked applicability review identifies an unpublished owner-issued grant as necessary.", "owner-issued-grant", "license-owner"),
            "incompatible-dependency-license" or "dependencies-policy-satisfied" => ((string?)null, null, null),
            _ => throw new InvalidOperationException($"Unknown LP-03 branch: {branch}")
        };
        var finding = branch switch
        {
            "dependencies-policy-satisfied" =>
                "Within the exact root and requested target-framework union, every distinct dependency " +
                "version has checked bytes and caller-prevalidated applicable grant evidence for a pinned " +
                "OSI-approved, non-copyleft single license; an empty checked closure is vacuously satisfied. " +
                "Acquisition completeness and review authority are accepted evidence boundaries, not independently attested.",
            "incompatible-dependency-license" =>
                "At least one exact dependency version in the checked target-framework union has " +
                "caller-prevalidated applicable grant evidence for a license incompatible with the pinned " +
                "OSI-approved, non-copyleft policy. This is a scoped LP-03 gap, not a legal opinion.",
            _ => null
        };
        return new(RowId, ResolverId, Version, branch, finding is null ? "escalated" : "deterministic",
            finding is not null || branch == "owner-evidence-required" ? "applicable" : "unknown",
            input.Inputs, input.Inputs.Select(i => $"input:{i.Role}:{i.Sha256}").ToArray(),
            Contract, finding, missing, required, next);
    }
}
