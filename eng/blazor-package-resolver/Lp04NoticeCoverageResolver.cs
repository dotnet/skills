using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BlazorPackageResolver;

public sealed class Lp04NoticeCoverageResolver : IDeterministicResolver
{
    private const string Contract =
        "Qualified exact-byte notice coverage for the checked root and explicit TFM/all-root-entry scope. " +
        "Admitted complete dependency and asset captures, prevalidated whole-subject obligation reviews, " +
        "and exhaustive reviewed mappings are trust boundaries, not authenticated by hashes or references. " +
        "No legal advice, LP-03 acceptability, build/source correspondence, ownership, vulnerability/support " +
        "conclusion, or proof that the supplied obligation review is legally correct.";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Dictionary<string, string> DocumentRoles = new(StringComparer.Ordinal)
    {
        ["dependencies"] = "dependency-inventory", ["assets"] = "asset-inventory",
        ["reviews"] = "obligation-reviews", ["mapping"] = "notice-mapping"
    };
    private static readonly HashSet<string> BaseRoles = new(
        ["package", "assessment", "notice-coverage", "admission", .. DocumentRoles.Values], StringComparer.Ordinal);
    private static readonly Regex IndexedRole = new(@"\A(?:dependency|asset|reference|entry)-[0-9]{3}\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex Digest = new(@"\A[a-f0-9]{64}\z", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> WeakeningRules = new("""
        absence-is-unresolved absent-shape admission-document-bindings admission-packet-binding
        admission-profiles admission-scope-binding aggregate-admission aggregate-bindings
        aggregate-byte-limit aggregate-segment aggregate-unique all-root-entries all-tfm-versions
        archive-byte-limit asset-checked-digest asset-classification asset-entry-binding
        asset-entry-unique asset-inventory-equality asset-inventory-unique asset-key asset-role
        asset-scope asset-source-binding capture-profiles capture-success dependency-digest
        dependency-identity-binding dependency-key dependency-role dependency-safety document-digests
        document-roots early-missing entry-bytes entry-inventory-digests entry-inventory-unique
        entry-kind-enum entry-kind-subjects entry-presence entry-range entry-reference
        entry-source-binding entry-subject-unique evidence-digest evidence-path evidence-role
        exact-fields extra-evidence gap-before-owner gap-before-review gap-before-unresolved
        graph-counts graph-diagnostics graph-edge-limit graph-edge-membership graph-reachability
        graph-unique-adjacency graph-unique-direct graph-unique-nodes graph-version-uniqueness
        inventory-equality inventory-unique json-byte-limit mapping-document mapping-equality
        mapping-parts mapping-state mapping-subjects mapping-unique modified-text no-path-aliases
        nonempty-obligation-parts obligations-unique owner-empty-obligations owner-fact owner-fact-scope
        owner-recognition packet-protocol part-bytes partial-content partial-is-unresolved
        partial-nonempty partial-proper-length parts-unique retained-byte-limit review-basis
        review-disposition review-disposition-enum review-obligation-form review-subject-binding
        reviews-coverage reviews-document reviews-inventory reviews-state reviews-unique root-digest
        root-entry-limit root-identity safe-paths schema-version scope-digest scope-nonempty
        scope-unique subject-bytes-required tfm-scope unique-json-fields unique-package-paths
        """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    public string RowId => "LP-04";
    public string ResolverId => "lp04-notice-coverage";
    public string Version => "1.0.0";
    public CaseResult Resolve(ResolverCase input) => Result(input, Classify(input.CheckedInputs, null));

    // Counterfactuals are friend-assembly test entry points; dispatch never supplies a weakening.
    internal CaseResult ResolveWeakening(ResolverCase input, string weakened)
    {
        Require(WeakeningRules.Contains(weakened), "unknown-mutation");
        return Result(input, Classify(input.CheckedInputs, weakened));
    }

    internal static bool SafePath(string? value) => Lp02PublicSourceResolver.SafePath(value) &&
        value!.Split('/').All(part => !part.EndsWith('.') && !part.EndsWith(' ') &&
            !Regex.IsMatch(part.Split('.')[0], @"\A(?:con|prn|aux|nul|com[1-9]|lpt[1-9])\z",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    private static string S(JsonNode? value)
    {
        Require(value is JsonValue scalar && scalar.TryGetValue<string>(out _), "string");
        return value.GetValue<string>();
    }

    private static long Number(JsonNode? value)
    {
        Require(value is JsonValue scalar && scalar.TryGetValue<long>(out _), "integer");
        return value.GetValue<long>();
    }

    private static JsonArray Array(JsonNode? value, int maximum = 64)
    {
        Require(value is JsonArray array && array.Count <= maximum, "array-limit");
        return (JsonArray)value;
    }

    private static void Fields([NotNull] JsonNode? value, string names, string? weakened)
    {
        Require(value is JsonObject, "invalid-fields");
        var expected = names.Split(' ').ToHashSet(StringComparer.Ordinal);
        var actual = ((JsonObject)value).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        Require(actual.SetEquals(expected) || weakened == "exact-fields" && expected.IsSubsetOf(actual), "invalid-fields");
    }

    private static void Text(string value, int maximum = 256) => Require(
        value.Length is > 0 && value.Length <= maximum && value.Any(ch => ch != ' ') &&
        value.All(ch => ch is >= ' ' and <= '~'), "invalid-text");
    private static void Sha(JsonNode? value) => Require(Digest.IsMatch(S(value)), "invalid-sha256");
    private static void Token(JsonNode? value) => Require(Lp03DependencyLicensesResolver.ValidId(S(value)), "invalid-token");
    private static void PathField(JsonNode? value, string? weakened) =>
        Require(weakened == "safe-paths" || SafePath(S(value)), "unsafe-path");
    private static void Identity(JsonNode? value, string? weakened)
    {
        Fields(value, "id version sha256", weakened);
        Token(value["id"]);
        Require(Lp03DependencyLicensesResolver.ValidVersion(S(value["version"])), "invalid-exact-version");
        Sha(value["sha256"]);
    }

    private static JsonNode? Read(ReadOnlyMemory<byte> data, string? weakened)
    {
        Require((weakened == "json-byte-limit" || data.Length <= 65536) &&
            !data.Span.StartsWith(new byte[] { 239, 187, 191 }), "document-limit-or-bom");
        using var document = JsonDocument.Parse(Utf8.GetString(data.Span), new() { MaxDepth = 10 });
        JsonNode? Convert(JsonElement item, int depth)
        {
            Require(depth <= 8, "document-depth");
            switch (item.ValueKind)
            {
                case JsonValueKind.Object:
                    var result = new JsonObject();
                    foreach (var property in item.EnumerateObject())
                    {
                        Text(property.Name);
                        Require(weakened == "unique-json-fields" || !result.ContainsKey(property.Name), "duplicate-field");
                        result[property.Name] = Convert(property.Value, depth + 1);
                    }
                    return result;
                case JsonValueKind.Array:
                    return new JsonArray(item.EnumerateArray().Select(child => Convert(child, depth + 1)).ToArray());
                case JsonValueKind.String:
                    var text = item.GetString()!;
                    Text(text, 1024);
                    return JsonValue.Create(text);
                case JsonValueKind.Number:
                    Require(Regex.IsMatch(item.GetRawText(), @"\A-?[0-9]+\z", RegexOptions.CultureInvariant),
                        "unsupported-scalar");
                    return JsonNode.Parse(item.GetRawText());
                case JsonValueKind.Null: return null;
                default: throw new InvalidDataException("unsupported-scalar");
            }
        }
        return Convert(document.RootElement, 0);
    }

    private static void EvidenceShape(JsonNode? value, string? weakened, string kind)
    {
        Fields(value, "role path sha256", weakened);
        Require(Regex.IsMatch(S(value["role"]), @"\A" + kind + @"-[0-9]{3}\z",
            RegexOptions.CultureInvariant), "evidence-role");
        PathField(value["path"], weakened);
        Sha(value["sha256"]);
    }

    private static void Shapes(Dictionary<string, JsonNode?> docs,
        IReadOnlyDictionary<string, CheckedInput> inputs, string? w)
    {
        foreach (var (role, input) in inputs)
        {
            Require(BaseRoles.Contains(role) || IndexedRole.IsMatch(role), "unknown-role");
            Require(SafePath(input.Identity.Path), "checked-path");
            var data = input.Content;
            if (role == "package" || role.StartsWith("dependency-", StringComparison.Ordinal) && role != "dependency-inventory")
                Require(data.Length > 0 && (w == "archive-byte-limit" || data.Length <= 8 * 1024 * 1024), "archive-limit");
            else if ((role.StartsWith("reference-", StringComparison.Ordinal) ||
                role.StartsWith("entry-", StringComparison.Ordinal) ||
                role.StartsWith("asset-", StringComparison.Ordinal)) && role != "asset-inventory")
            {
                var asset = role.StartsWith("asset-", StringComparison.Ordinal);
                var empty = w == "partial-nonempty" && role.StartsWith("entry-", StringComparison.Ordinal) && data.Length == 0;
                Require((data.Length > 0 || empty) &&
                    (data.Length <= (asset ? 1024 * 1024 : 16384) || w == "retained-byte-limit"), "retained-byte-limit");
                if (!asset)
                    Require(empty || !data.Span.StartsWith(new byte[] { 239, 187, 191 }) &&
                        !Lp01LicensePolicyResolver.PythonBlank(Utf8.GetString(data.Span)), "retained-text");
            }
        }
        Require(w == "aggregate-byte-limit" || inputs.Values.Sum(value => (long)value.Content.Length) <= 64 * 1024 * 1024,
            "total-byte-limit");
        foreach (var (role, doc) in docs)
        {
            if (doc is null) continue;
            Require(doc is JsonObject, "schema-version");
            var schema = Number(doc["schema_version"]);
            Require(w == "schema-version" || schema == 1, "schema-version");
            if (role is not ("obligation-reviews" or "notice-mapping")) Identity(doc["root"], w);
        }
        var scope = docs["assessment"];
        Fields(scope, "schema_version root target_frameworks asset_scope", w);
        var tfms = Array(scope["target_frameworks"], 4);
        Require((w == "scope-nonempty" || tfms.Count > 0) &&
            (w == "scope-unique" || tfms.Select(S).Distinct(StringComparer.Ordinal).Count() == tfms.Count) &&
            (w == "asset-scope" || S(scope["asset_scope"]) == "all-root-package-entries"), "assessment-scope");
        foreach (var tfm in tfms) Token(tfm);
        if (docs["notice-coverage"] is { } packet)
        {
            Fields(packet, "schema_version protocol root assessment_sha256 dependencies assets reviews mapping admission_sha256", w);
            Require(w == "packet-protocol" || S(packet["protocol"]) == "notice-coverage-v1", "packet-protocol");
            Sha(packet["assessment_sha256"]);
            Sha(packet["admission_sha256"]);
            foreach (var field in DocumentRoles.Keys) if (packet[field] is not null) Sha(packet[field]);
        }
        if (docs["admission"] is { } admission)
        {
            Fields(admission, "schema_version profile root assessment_sha256 dependencies assets reviews mapping", w);
            Require(w == "admission-profiles" || S(admission["profile"]) == "notice-coverage-admission/1", "admission-profile");
            Sha(admission["assessment_sha256"]);
            foreach (var name in DocumentRoles.Keys)
            {
                var item = admission[name];
                Fields(item, "profile sha256 reference authority_reference state aggregates", w);
                var profile = name switch
                {
                    "dependencies" => "resolved-target-capture/1", "assets" => "all-root-entry-capture/1",
                    "reviews" => "whole-subject-obligations/1", _ => "exhaustive-notice-mapping/1"
                };
                Require(w == "capture-profiles" || S(item["profile"]) == profile, "capture-profile");
                if (item["sha256"] is not null) Sha(item["sha256"]);
                Text(S(item["reference"]));
                Text(S(item["authority_reference"]));
                Require(S(item["state"]) is "captured" or "unavailable", "capture-state");
                var aggregates = Array(item["aggregates"]);
                Require(name == "mapping" || aggregates.Count == 0, "aggregate-scope");
                foreach (var aggregate in aggregates)
                {
                    Fields(aggregate, "path sha256", w);
                    PathField(aggregate["path"], w);
                    Sha(aggregate["sha256"]);
                }
            }
        }
        if (docs["dependency-inventory"] is { } deps)
        {
            Fields(deps, "schema_version profile root targets packages unresolved", w);
            Require(S(deps["profile"]) == "resolved-target-graph/1", "dependency-profile");
            Array(deps["targets"], 4);
            Array(deps["packages"]);
            foreach (var diagnostic in Array(deps["unresolved"])) Text(S(diagnostic));
            foreach (var target in deps["targets"]!.AsArray())
            {
                Fields(target, "tfm direct nodes node_count edge_count", w);
                Token(target["tfm"]);
                Array(target["direct"]);
                Array(target["nodes"]);
                Require(Number(target["node_count"]) is >= 0 and <= 64 && Number(target["edge_count"]) >= 0 &&
                    (Number(target["edge_count"]) <= 128 || w == "graph-edge-limit"), "graph-count-limit");
                foreach (var node in target["nodes"]!.AsArray())
                {
                    Fields(node, "key dependencies", w);
                    Text(S(node["key"]), 128);
                    foreach (var edge in Array(node["dependencies"])) Text(S(edge), 128);
                }
                foreach (var edge in target["direct"]!.AsArray()) Text(S(edge), 128);
            }
            foreach (var package in deps["packages"]!.AsArray())
            {
                Fields(package, "key identity role", w);
                Text(S(package["key"]), 128);
                Identity(package["identity"], w);
                Require(Regex.IsMatch(S(package["role"]), @"\Adependency-[0-9]{3}\z", RegexOptions.CultureInvariant), "dependency-role");
            }
        }
        if (docs["asset-inventory"] is { } assets)
        {
            Fields(assets, "schema_version root entries assets", w);
            foreach (var entry in Array(assets["entries"], w == "root-entry-limit" ? int.MaxValue : 128))
            {
                Fields(entry, "path sha256 kind subjects", w);
                PathField(entry["path"], w);
                Sha(entry["sha256"]);
                Require(w == "entry-kind-enum" || S(entry["kind"]) is "third-party" or "first-party" or "packaging" or "unresolved", "entry-kind");
                foreach (var subject in Array(entry["subjects"])) Text(S(subject), 128);
            }
            foreach (var asset in Array(assets["assets"]))
            {
                Fields(asset, "key role package_entry sha256", w);
                Text(S(asset["key"]), 128);
                PathField(asset["package_entry"], w);
                Sha(asset["sha256"]);
                Require(Regex.IsMatch(S(asset["role"]), @"\Aasset-[0-9]{3}\z", RegexOptions.CultureInvariant), "asset-role");
            }
        }
        if (docs["obligation-reviews"] is { } reviews)
        {
            Fields(reviews, "schema_version reviews", w);
            foreach (var review in Array(reviews["reviews"], 128))
            {
                Fields(review, "subject sha256 disposition basis obligations owner_fact", w);
                Text(S(review["subject"]), 128);
                Sha(review["sha256"]);
                Require(w == "review-disposition-enum" || S(review["disposition"]) is
                    "obligations" or "no-obligations" or "unresolved" or "unsupported" or "owner-private", "review-disposition");
                EvidenceShape(review["basis"], w, "reference");
                if (review["owner_fact"] is not null) Text(S(review["owner_fact"]));
                foreach (var obligation in Array(review["obligations"], 8))
                {
                    Fields(obligation, "id parts", w);
                    Token(obligation["id"]);
                    var parts = Array(obligation["parts"], 8);
                    Require(w == "nonempty-obligation-parts" || parts.Count > 0, "empty-obligation");
                    foreach (var part in parts)
                    {
                        Fields(part, "id kind evidence", w);
                        Token(part["id"]);
                        Require(S(part["kind"]) is "license-text" or "notice" or "attribution", "obligation-kind");
                        EvidenceShape(part["evidence"], w, "reference");
                    }
                }
            }
        }
        if (docs["notice-mapping"] is { } mapping)
        {
            Fields(mapping, "schema_version mappings", w);
            foreach (var item in Array(mapping["mappings"], 128))
            {
                Fields(item, "subject obligation part mode package_entry entry offset length", w);
                foreach (var field in new[] { "subject", "obligation", "part" }) Text(S(item[field]), 128);
                Require(S(item["mode"]) is "entry" or "aggregate" or "absent" or "partial", "mapping-mode");
                PathField(item["package_entry"], w);
                if (item["entry"] is not null) EvidenceShape(item["entry"], w, "entry");
                Require(Number(item["offset"]) is >= 0 and <= 16384 &&
                    (Number(item["length"]) is >= 0 and <= 16384 ||
                    w == "retained-byte-limit" && Number(item["length"]) >= 0), "mapping-range");
            }
        }
    }

    private static Dictionary<string, JsonNode> Map(JsonArray? array, string key) =>
        (array ?? []).Where(item => item is not null).GroupBy(item => S(item![key]), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last()!, StringComparer.Ordinal);
    private static bool EqualKeys<T, U>(Dictionary<string, T> left, Dictionary<string, U> right) =>
        left.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(right.Keys);
    private static string[] Strings(JsonNode? node) => ((JsonArray)node!).Select(S).ToArray();

    private static string Classify(IReadOnlyDictionary<string, CheckedInput> inputs, string? w)
    {
        if (!inputs.TryGetValue("package", out var package)) return "missing-package";
        if (!inputs.ContainsKey("assessment")) return "scope-missing";
        Dictionary<string, JsonNode?> docs;
        try
        {
            docs = BaseRoles.Where(role => role != "package").ToDictionary(role => role,
                role => inputs.TryGetValue(role, out var item) ? Read(item.Content, w) : null, StringComparer.Ordinal);
            Shapes(docs, inputs, w);
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or DecoderFallbackException or InvalidOperationException)
        {
            return "input-invalid";
        }
        var scope = docs["assessment"]!;
        var selected = w is "entry-reference" or "entry-bytes"
            ? docs["notice-mapping"]?["mappings"]!.AsArray()
                .Select(item => S(item!["package_entry"])).ToHashSet(StringComparer.Ordinal) ?? []
            : new HashSet<string>(StringComparer.Ordinal);
        var root = Lp04PackageEvidence.Read(package.Content, selected, w);
        if (root is null) return "input-invalid";
        if (w != "unique-package-paths" && root.Duplicates || w != "no-path-aliases" && root.Aliases || !root.Safe)
            return "input-invalid";
        if (w != "root-digest" && S(scope["root"]!["sha256"]) != package.Identity.Sha256 ||
            w != "root-identity" && (S(scope["root"]!["id"]) != root.Id || S(scope["root"]!["version"]) != root.Version))
            return "binding-conflict";
        var packet = docs["notice-coverage"];
        var admission = docs["admission"];
        if (packet is null || admission is null || docs["dependency-inventory"] is null || docs["asset-inventory"] is null)
            return "evidence-missing";
        foreach (var doc in new[] { packet, admission, docs["dependency-inventory"]!, docs["asset-inventory"]! })
            if (w != "document-roots" && !JsonNode.DeepEquals(doc["root"], scope["root"])) return "binding-conflict";
        if (w != "scope-digest" && S(packet["assessment_sha256"]) != inputs["assessment"].Identity.Sha256 ||
            w != "admission-scope-binding" && S(admission["assessment_sha256"]) != inputs["assessment"].Identity.Sha256 ||
            w != "admission-packet-binding" && S(packet["admission_sha256"]) != inputs["admission"].Identity.Sha256)
            return "binding-conflict";
        foreach (var (label, role) in DocumentRoles)
        {
            var actual = inputs.TryGetValue(role, out var value) ? value.Identity.Sha256 : null;
            if (w != "document-digests" && packet[label]?.GetValue<string>() != actual ||
                w != "admission-document-bindings" && admission[label]!["sha256"]?.GetValue<string>() != actual)
                return "binding-conflict";
        }
        var deps = docs["dependency-inventory"]!;
        var assets = docs["asset-inventory"]!;
        if (w != "capture-success" && new[] { "dependencies", "assets" }.Any(name => S(admission[name]!["state"]) != "captured"))
            return "inventories-unresolved";
        if (w != "tfm-scope" && !deps["targets"]!.AsArray().Select(t => S(t!["tfm"])).Order(StringComparer.Ordinal)
                .SequenceEqual(Strings(scope["target_frameworks"]).Order(StringComparer.Ordinal)) ||
            w != "graph-diagnostics" && deps["unresolved"]!.AsArray().Count > 0)
            return "inventories-unresolved";
        var union = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in deps["targets"]!.AsArray())
        {
            var nodes = Map(target!["nodes"]!.AsArray(), "key");
            var direct = Strings(target["direct"]);
            var edges = direct.Concat(nodes.Values.SelectMany(n => Strings(n["dependencies"]))).ToArray();
            if (w != "graph-unique-nodes" && nodes.Count != target["nodes"]!.AsArray().Count ||
                w != "graph-unique-direct" && direct.Distinct(StringComparer.Ordinal).Count() != direct.Length ||
                w != "graph-unique-adjacency" && nodes.Values.Any(n => Strings(n["dependencies"]).Distinct(StringComparer.Ordinal).Count() != n["dependencies"]!.AsArray().Count) ||
                w != "graph-edge-limit" && edges.Length > 128 ||
                w != "graph-edge-membership" && edges.Any(edge => !nodes.ContainsKey(edge)) ||
                w != "graph-version-uniqueness" && nodes.Keys.Select(key => key.Split('/')[0]).Distinct(StringComparer.Ordinal).Count() != nodes.Count)
                return "inventories-unresolved";
            var reached = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>(direct);
            while (pending.TryPop(out var key))
            {
                if (w == "graph-edge-membership" && !nodes.ContainsKey(key)) continue;
                if (reached.Add(key)) foreach (var edge in Strings(nodes[key]["dependencies"])) pending.Push(edge);
            }
            if (w != "graph-reachability" && !reached.SetEquals(nodes.Keys) ||
                w != "graph-counts" && (Number(target["node_count"]) != nodes.Count || Number(target["edge_count"]) != edges.Length))
                return "inventories-unresolved";
            union.UnionWith(nodes.Keys);
        }
        var packages = Map(deps["packages"]!.AsArray(), "key");
        if (union.Count > 64 || w != "inventory-equality" && !union.SetEquals(packages.Keys) ||
            w != "inventory-unique" && packages.Count != deps["packages"]!.AsArray().Count)
            return "inventories-unresolved";
        var entries = Map(assets["entries"]!.AsArray(), "path");
        if (w != "entry-inventory-unique" && entries.Count != assets["entries"]!.AsArray().Count ||
            w != "all-root-entries" && !entries.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(root.Entries.Keys))
            return "inventories-unresolved";
        if (w != "entry-inventory-digests" && entries.Any(pair =>
            !root.Entries.TryGetValue(pair.Key, out var observed) || S(pair.Value["sha256"]) != observed.Sha256))
            return "binding-conflict";
        if (w != "asset-classification" && entries.Values.Any(item => S(item["kind"]) == "unresolved"))
            return "inventories-unresolved";
        var assetMap = Map(assets["assets"]!.AsArray(), "key");
        if (w != "asset-inventory-unique" && assetMap.Count != assets["assets"]!.AsArray().Count)
            return "inventories-unresolved";
        var classified = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in entries.Values)
        {
            var names = Strings(item["subjects"]);
            if (w != "entry-subject-unique" && names.Distinct(StringComparer.Ordinal).Count() != names.Length ||
                w != "entry-kind-subjects" && (S(item["kind"]) == "third-party") != (names.Length > 0))
                return "inventories-unresolved";
            foreach (var subject in names)
            {
                if (classified.ContainsKey(subject) && w is not ("entry-subject-unique" or "asset-entry-unique"))
                    return "inventories-unresolved";
                classified[subject] = S(item["path"]);
            }
        }
        if (w != "asset-inventory-equality" && !classified.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(assetMap.Keys))
            return "inventories-unresolved";
        var subjects = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = BaseRoles.Intersect(inputs.Keys).ToHashSet(StringComparer.Ordinal);
        var missing = false;
        var index = 0;
        foreach (var (key, item) in packages.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var identity = item["identity"]!;
            var role = S(item["role"]);
            var expectedRole = "dependency-" + (index++).ToString("D3", CultureInfo.InvariantCulture);
            if (w != "dependency-key" && key != $"{S(identity["id"])}/{S(identity["version"])}" ||
                w != "dependency-role" && role != expectedRole) return "binding-conflict";
            subjects["dependency:" + key] = S(identity["sha256"]);
            used.Add(role);
            if (!inputs.TryGetValue(role, out var value))
            {
                missing = true;
                if (w == "early-missing") return "subject-bytes-missing";
                continue;
            }
            var observed = Lp04PackageEvidence.Read(value.Content, [], w);
            if (observed is null || w != "dependency-safety" && (observed.Duplicates || observed.Aliases || !observed.Safe))
                return "input-invalid";
            if (w != "dependency-digest" && value.Identity.Sha256 != S(identity["sha256"]) ||
                w != "dependency-identity-binding" && (observed.Id != S(identity["id"]) || observed.Version != S(identity["version"])))
                return "binding-conflict";
        }
        index = 0;
        foreach (var (key, item) in assetMap.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var role = S(item["role"]);
            var path = S(item["package_entry"]);
            if (w != "asset-key" && !key.StartsWith("asset:", StringComparison.Ordinal) ||
                w != "asset-role" && role != "asset-" + (index++).ToString("D3", CultureInfo.InvariantCulture) ||
                w != "asset-entry-binding" && classified[key] != path) return "binding-conflict";
            if (w != "asset-source-binding" && S(item["sha256"]) != root.Entries[path].Sha256) return "binding-conflict";
            subjects[key] = S(item["sha256"]);
            used.Add(role);
            if (!inputs.TryGetValue(role, out var value)) missing = true;
            else if (w != "asset-checked-digest" && value.Identity.Sha256 != S(item["sha256"])) return "binding-conflict";
        }
        var reviewsDoc = docs["obligation-reviews"];
        var reviews = Map(reviewsDoc?["reviews"]!.AsArray(), "subject");
        if (w != "reviews-unique" && reviews.Count != (reviewsDoc?["reviews"]!.AsArray().Count ?? 0) ||
            w != "reviews-inventory" && reviews.Keys.Any(subject => !subjects.ContainsKey(subject)))
            return "binding-conflict";
        if (w == "reviews-inventory") reviews = reviews.Where(pair => subjects.ContainsKey(pair.Key)).ToDictionary();
        var reviewUnresolved = w != "reviews-document" && reviewsDoc is null ||
            w != "reviews-coverage" && !reviews.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(subjects.Keys) ||
            w != "reviews-state" && S(admission["reviews"]!["state"]) != "captured";
        var owner = false;
        var parts = new Dictionary<(string Subject, string Obligation, string Part), byte[]?>();
        byte[]? Evidence(JsonNode value)
        {
            var role = S(value["role"]);
            used.Add(role);
            if (!inputs.TryGetValue(role, out var actual))
            {
                if (w == "evidence-role")
                    foreach (var input in inputs.Values)
                        if (input.Identity.Path == S(value["path"]) && input.Identity.Sha256 == S(value["sha256"]))
                        {
                            used.Add(input.Identity.Role);
                            return input.Content.ToArray();
                        }
                return null;
            }
            Require((w == "evidence-path" || actual.Identity.Path == S(value["path"])) &&
                (w == "evidence-digest" || actual.Identity.Sha256 == S(value["sha256"])), "binding-conflict");
            return actual.Content.ToArray();
        }
        bool mappingUnresolved, gap;
        try
        {
            foreach (var (subject, review) in reviews.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (w != "review-subject-binding" && S(review["sha256"]) != subjects[subject]) return "binding-conflict";
                var basis = Evidence(review["basis"]!);
                var disposition = S(review["disposition"]);
                if (w == "all-tfm-versions" && subject == "dependency:sample.a/2.0.0") disposition = "obligations";
                if (basis is null && w != "review-basis" || disposition is "unresolved" or "unsupported" && w != "review-disposition")
                    reviewUnresolved = true;
                if (disposition == "owner-private")
                {
                    if (basis is null && w != "review-basis" ||
                        w != "owner-fact" && review["owner_fact"] is null ||
                        w != "owner-empty-obligations" && review["obligations"]!.AsArray().Count > 0) reviewUnresolved = true;
                    else owner = w != "owner-recognition";
                }
                else if (w != "owner-fact-scope" && review["owner_fact"] is not null) return "binding-conflict";
                var obligations = review["obligations"]!.AsArray().Select(item => item!).ToArray();
                if (w != "review-obligation-form" && (disposition == "no-obligations" && obligations.Length > 0 ||
                    disposition == "obligations" && obligations.Length == 0)) return "binding-conflict";
                if (w == "obligations-unique") obligations = obligations.GroupBy(item => S(item["id"]), StringComparer.Ordinal)
                    .Select(group => group.Last()).ToArray();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var obligation in obligations)
                {
                    if (!ids.Add(S(obligation["id"]))) return "binding-conflict";
                    foreach (var part in obligation["parts"]!.AsArray())
                    {
                        var key = (subject, S(obligation["id"]), S(part!["id"]));
                        if (w != "parts-unique" && parts.ContainsKey(key)) return "binding-conflict";
                        parts[key] = Evidence(part["evidence"]!);
                        if (w != "part-bytes" && parts[key] is null) reviewUnresolved = true;
                    }
                }
            }
            var mappingDoc = docs["notice-mapping"];
            var mappings = (mappingDoc?["mappings"]!.AsArray() ?? []).Select(item => item!)
                .GroupBy(item => (S(item["subject"]), S(item["obligation"]), S(item["part"])))
                .ToDictionary(group => group.Key, group => group.Last());
            if (w != "mapping-unique" && mappings.Count != (mappingDoc?["mappings"]!.AsArray().Count ?? 0) ||
                w != "mapping-parts" && mappings.Keys.Any(key => !parts.ContainsKey(key) && reviews.ContainsKey(key.Item1)) ||
                w != "mapping-subjects" && mappings.Keys.Any(key => !subjects.ContainsKey(key.Item1))) return "binding-conflict";
            if (w == "mapping-parts") mappings = mappings.Where(pair => parts.ContainsKey(pair.Key)).ToDictionary();
            if (w == "mapping-subjects") mappings = mappings.Where(pair => subjects.ContainsKey(pair.Key.Item1)).ToDictionary();
            mappingUnresolved = w != "mapping-document" && mappingDoc is null ||
                w != "mapping-state" && S(admission["mapping"]!["state"]) != "captured" ||
                w != "mapping-equality" && !mappings.Keys.ToHashSet().SetEquals(parts.Keys);
            var accepted = Map(admission["mapping"]!["aggregates"]!.AsArray(), "path");
            if (w != "aggregate-unique" && accepted.Count != admission["mapping"]!["aggregates"]!.AsArray().Count)
                return "binding-conflict";
            foreach (var (path, entry) in accepted)
                if (w != "aggregate-bindings" && (!root.Entries.TryGetValue(path, out var observed) ||
                    observed.Sha256 != S(entry["sha256"]))) return "binding-conflict";
            gap = false;
            foreach (var (key, item) in mappings)
            {
                root.Entries.TryGetValue(S(item["package_entry"]), out var packaged);
                var mode = S(item["mode"]);
                if (mode == "absent")
                {
                    if (w != "absent-shape" && (item["entry"] is not null || Number(item["offset"]) != 0 ||
                        Number(item["length"]) != 0 || packaged is not null)) return "binding-conflict";
                    if (w == "absence-is-unresolved") mappingUnresolved = true;
                    else gap = true;
                    continue;
                }
                byte[]? retained;
                if (item["entry"] is null)
                {
                    if (w != "entry-reference" || packaged is null)
                    {
                        mappingUnresolved = true;
                        continue;
                    }
                    retained = packaged.Content;
                }
                else retained = Evidence(item["entry"]!);
                if (w != "entry-presence" && packaged is null ||
                    packaged is not null && item["entry"] is not null && w != "entry-source-binding" &&
                    packaged.Sha256 != S(item["entry"]!["sha256"])) return "binding-conflict";
                if (retained is null)
                {
                    if (w != "entry-bytes")
                    {
                        mappingUnresolved = true;
                        continue;
                    }
                    retained = packaged?.Content;
                }
                Require(retained is not null, "binding-conflict");
                if (w != "entry-range" && mode != "aggregate" &&
                    (Number(item["offset"]) != 0 || Number(item["length"]) != retained.Length)) return "binding-conflict";
                if (!parts.TryGetValue(key, out var expected) || expected is null) continue;
                if (mode == "aggregate")
                {
                    if (w != "aggregate-admission" && (!accepted.TryGetValue(S(item["package_entry"]), out var aggregate) ||
                        S(aggregate["sha256"]) != S(item["entry"]!["sha256"]))) mappingUnresolved = true;
                    var offset = (int)Number(item["offset"]);
                    var length = (int)Number(item["length"]);
                    if (w != "aggregate-segment" && (length != expected.Length || offset > retained.Length ||
                        length > retained.Length - offset || !retained.AsSpan(offset, length).SequenceEqual(expected)))
                        mappingUnresolved = true;
                }
                else if (mode == "partial")
                {
                    if ((w == "partial-nonempty" || retained.Length > 0) &&
                        (w == "partial-proper-length" || retained.Length < expected.Length) &&
                        (w == "partial-content" || expected.AsSpan().StartsWith(retained)))
                    {
                        if (w == "partial-is-unresolved") mappingUnresolved = true;
                        else gap = true;
                    }
                    else mappingUnresolved = true;
                }
                else if (w != "modified-text" && !retained.AsSpan().SequenceEqual(expected)) mappingUnresolved = true;
            }
        }
        catch (InvalidDataException error) when (error.Message == "binding-conflict") { return "binding-conflict"; }
        if (w != "extra-evidence" && inputs.Keys.Any(role => !used.Contains(role))) return "binding-conflict";
        if (w != "subject-bytes-required" && missing) return "subject-bytes-missing";
        if (reviewUnresolved && !(gap && w == "gap-before-review")) return "reviews-unresolved";
        if (mappingUnresolved && !(gap && w == "gap-before-unresolved")) return "mapping-unresolved";
        if (owner && !(gap && w == "gap-before-owner")) return "owner-evidence-required";
        if (gap) return "required-representation-missing";
        return parts.Count > 0 ? "notice-coverage-satisfied" : "no-applicable-surface";
    }

    private CaseResult Result(ResolverCase input, string branch)
    {
        var (missing, required, next) = branch switch
        {
            "missing-package" => ("The exact root package is missing.", "package", "package-acquisition"),
            "scope-missing" => ("An explicit exact-root and assessment scope is missing.", "assessment", "assessment-scope"),
            "evidence-missing" => ("The notice-coverage packet or an inventory/admission document is missing.", "notice-coverage-v1", "notice-acquisition"),
            "input-invalid" => ("Evidence is outside the bounded unambiguous supported profile.", "supported-evidence", "evidence-repair"),
            "binding-conflict" => ("Checked root, inventory, subject, review or packaged-entry bindings disagree.", "consistent-bindings", "evidence-repair"),
            "inventories-unresolved" => ("Complete resolved dependencies and all-root-entry asset inspection are not established.", "complete-inventories", "notice-acquisition"),
            "subject-bytes-missing" => ("An exact inventoried dependency or bundled asset is unavailable.", "subject-bytes", "notice-acquisition"),
            "reviews-unresolved" => ("Whole-subject applicability or supported retained obligation evidence is incomplete.", "obligation-reviews", "notice-investigation"),
            "mapping-unresolved" => ("Exhaustive reviewed mappings or supported exact representations are incomplete.", "notice-mapping", "notice-investigation"),
            "owner-evidence-required" => ("An admitted review establishes a genuinely private owner fact necessary to resolve an obligation.", "private-owner-fact", "notice-owner"),
            "required-representation-missing" or "notice-coverage-satisfied" or "no-applicable-surface" => ((string?)null, null, null),
            _ => throw new InvalidOperationException("Unknown LP-04 branch: " + branch)
        };
        var finding = branch switch
        {
            "notice-coverage-satisfied" => "Every applicable reviewed preservation part is represented by exact checked root entry bytes or explicitly admitted aggregate segments within the assessed scope.",
            "required-representation-missing" => "Complete admitted inventories, supported obligation reviews and exhaustive mappings establish at least one required representation absent or a strict nonempty prefix in the exact root package.",
            "no-applicable-surface" => "Complete admitted inventories and whole-subject reviews establish no applicable third-party license/notice preservation obligation within the assessed scope.",
            _ => null
        };
        return new(RowId, ResolverId, Version, branch, finding is null ? "escalated" : "deterministic",
            branch == "no-applicable-surface" ? "not-applicable" : finding is not null || branch == "owner-evidence-required" ? "applicable" : "unknown",
            input.Inputs, input.Inputs.OrderBy(pin => pin.Role, StringComparer.Ordinal).Select(pin => $"input:{pin.Role}:{pin.Sha256}").ToArray(),
            Contract, finding, missing, required, next);
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string rule)
    {
        if (!condition) throw new InvalidDataException(rule);
    }
}
