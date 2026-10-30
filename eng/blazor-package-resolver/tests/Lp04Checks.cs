using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using BlazorPackageResolver;

internal static class Lp04Checks
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    private static readonly Dictionary<string, string> FrozenHashes = new(StringComparer.Ordinal)
    {
        ["README.md"] = "86f626a780339b219837187fa4718e29bacf0195ba52f8c29298d4f4d38aa4c9",
        ["acceptance-manifest.json"] = "d3ae695c6cdca45df2ff9e9cdb5cc60f2f21343c4fe485eb1897935fe1710ff7",
        ["discrimination.json"] = "a799b51e9805a8e214e54860ad148eba795683ec00bdc1246d59f2df9ffc5d76",
        ["cases.json"] = "b6622f9adb92a0ba0d71e52903a0977976630129fb140bba363c36f3ad6eea9f",
        ["input-manifest.json"] = "2e3278df56eb90964fff2906376abb75fa0ad7affe6b4d88f01656e28d361d9c",
        [".gitattributes"] = "2545f57abbcb5aaedeca4a55bd298775a45f0d165f4923b829d1a1612e46b395",
        ["rejected-inputs.json"] = "216060b4ef561e170c70712889d0ab596830bf08102ca4a543ae737dfb8930ba",
        ["acceptance.json"] = "627096d0d39f11a1839903f4cac43216a36796ce8969ea59e9446074fc817421",
        ["retained-inputs.json"] = "f975e282d5216b9c20f849cdec393e5e80a05d8b50e9c73824f02a8e991cb87b"
    };
    private static readonly Dictionary<string, string> DocumentRoles = new(StringComparer.Ordinal)
    {
        ["dependencies"] = "dependency-inventory", ["assets"] = "asset-inventory",
        ["reviews"] = "obligation-reviews", ["mapping"] = "notice-mapping"
    };

    // Independent finite inventories: removing metadata cannot also remove its test obligation.
    private static readonly string[] CriticalGuards = Words("""
        root-required scope-required packet-required exact-positive captured-empty reviewed-none
        aggregate-segments direct-absence direct-prefix dependency-inventory-required graph-counts
        graph-diagnostics asset-inventory-required all-root-entries asset-classification
        dependency-bytes-required asset-bytes-required reviews-required review-disposition
        mapping-required mapping-equality root-identity root-digest scope-digest document-digests
        asset-source-binding review-subject-binding entry-source-binding exact-fields unique-json-fields
        safe-paths retained-byte-limit admission-profiles dependency-identity-binding modified-text
        successful-captures unique-package-paths no-path-aliases all-tfm-versions private-before-gap
        unresolved-before-gap bindings-before-missing aggregate-admission loader-duplicate-role
        loader-safe-path loader-file-presence loader-digest loader-exact-fields retained-syncfusion
        retained-infragistics reviews-document document-roots admission-scope-binding
        admission-packet-binding admission-document-bindings tfm-scope graph-unique-nodes
        graph-unique-direct graph-unique-adjacency graph-edge-limit graph-edge-membership
        graph-reachability graph-version-uniqueness inventory-equality inventory-unique
        entry-inventory-unique entry-inventory-digests asset-inventory-unique entry-subject-unique
        entry-kind-subjects asset-entry-unique asset-inventory-equality dependency-key dependency-role
        dependency-safety dependency-digest asset-key asset-role asset-entry-binding asset-checked-digest
        reviews-unique reviews-inventory reviews-coverage reviews-state review-basis evidence-path
        evidence-digest evidence-role owner-fact owner-empty-obligations owner-fact-scope
        owner-recognition review-obligation-form obligations-unique parts-unique part-bytes
        mapping-unique mapping-parts mapping-subjects mapping-document mapping-state aggregate-unique
        aggregate-bindings absent-shape entry-reference entry-presence entry-bytes entry-range
        aggregate-segment partial-proper-length partial-content partial-nonempty extra-evidence
        review-before-gap schema-version packet-protocol capture-profiles scope-nonempty scope-unique
        asset-scope json-byte-limit archive-byte-limit asset-byte-limit aggregate-byte-limit
        root-entry-limit nonempty-obligation-parts review-disposition-enum entry-kind-enum
        """);
    private static readonly string[] WeakeningLabels = Words("""
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
        """);
    private static readonly string[] FixedRecipes = Words("""
        omitted-empty-reviews packet-root admission-root dependency-root asset-root observed-root-id
        admission-scope admission-digest admitted-dependencies admitted-assets admitted-reviews
        admitted-mapping packet-assets packet-reviews packet-mapping asset-capture-unavailable
        target-set duplicate-target duplicate-node duplicate-direct duplicate-adjacency
        edge-count-discrepancy edge-limit dangling-direct dangling-transitive orphan-node
        competing-versions missing-package-inventory extra-package-inventory duplicate-package-inventory
        duplicate-entry-inventory entry-inventory-digest duplicate-asset-inventory duplicate-entry-subject
        unlisted-third-party first-party-with-subject unknown-entry-kind repeated-asset-location
        missing-listed-asset dependency-key dependency-role dependency-duplicate dependency-alias
        dependency-unsafe dependency-digest observed-dependency-id asset-key asset-role asset-entry
        asset-checked-digest duplicate-review extra-review out-of-version-review missing-one-review
        review-state no-obligations-missing-basis entry-reference-path basis-reference-path
        part-reference-path basis-reference-digest part-reference-digest entry-reference-role
        unsupported-only unresolved-only unknown-review-disposition owner-fact-missing owner-basis-missing
        owner-with-obligations owner-fact-without-disposition owner-disposition none-with-obligations
        required-without-obligations duplicate-obligation empty-obligation-parts duplicate-part
        missing-part-bytes duplicate-mapping extra-mapping-part extra-mapping-obligation
        out-of-inventory-mapping out-of-version-mapping omitted-empty-mapping mapping-state
        missing-subject-mapping missing-obligation-mapping missing-part-mapping duplicate-aggregate
        aggregate-digest aggregate-path absent-present-path absent-entry-reference absent-offset
        absent-length missing-entry-reference missing-packaged-entry missing-entry-bytes entry-offset
        entry-length aggregate-offset aggregate-length partial-exact partial-unrelated partial-empty
        extra-evidence reviews-before-gap schema-version packet-protocol capture-profile empty-assessment
        duplicate-assessment partial-asset-scope json-byte-limit archive-byte-limit asset-byte-limit
        aggregate-byte-limit root-entry-limit
        """);

    internal static void Run(Registry registry, Catalog catalog, string directory)
    {
        foreach (var (path, hash) in FrozenHashes)
            Require(Catalog.Digest(File.ReadAllBytes(Path.Combine(directory, path))) == hash, $"Phase A pin: {path}");
        CheckOracle();
        VerifyInputs(directory);
        var fixture = Path.Combine(directory, "cases.json");
        var suite = JsonSerializer.Deserialize<CaseSuite>(File.ReadAllBytes(fixture), Options)!;
        var retained = JsonSerializer.Deserialize<CaseSuite>(File.ReadAllBytes(Path.Combine(directory, "acceptance.json")), Options)!;
        var controls = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, "rejected-inputs.json")))!["controls"]!.AsArray();
        Require(suite.Cases.Count == 43 && retained.Cases.Count == 2 && controls.Count == 5 &&
            suite.Cases.Concat(retained.Cases).Select(c => c.CaseId)
                .Concat(controls.Select(c => Text(c!, "case_id"))).Distinct(StringComparer.Ordinal).Count() == 50,
            "Exactly 50 unchanged named expectations");
        var output = Replay.Run(fixture, registry);
        Require(output.SequenceEqual(Replay.Run(fixture, registry)), "Exact canonical replay determinism");
        Require(Catalog.Digest(output) == "129b1eb15ecf750062c4d787871d6ba51582b48ef2ed90b8bf79a8d60256ea6c",
            "Frozen canonical 43-case replay hash");
        var templates = suite.Cases.Concat(retained.Cases).Select(c => c.Expected)
            .GroupBy(c => c.BranchId).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        Require(templates.Count == 13, "All 13 canonical result contracts");
        var byId = suite.Cases.ToDictionary(c => c.CaseId, StringComparer.Ordinal);
        var spec = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, "discrimination.json")))!;
        ValidateInventory(spec);
        var executed = new HashSet<string>(StringComparer.Ordinal);
        var recipes = new HashSet<string>(StringComparer.Ordinal);
        var variants = new HashSet<string>(StringComparer.Ordinal);
        var weakeningRuns = 0;
        var existingRuns = 0;
        var resolver = new Lp04NoticeCoverageResolver();
        foreach (var node in spec["witnesses"]!.AsArray())
        {
            var witness = node!;
            var kind = Text(witness, "kind");
            if (kind is "loader" or "optional-retained") continue;
            var packet = Packet.Load(byId[Text(witness, "case_id")], directory);
            if (kind == "fixed-in-memory")
            {
                var recipe = Text(witness, "recipe");
                Require(recipes.Add(recipe), "Fixed recipe executed exactly once");
                new RecipePacket(packet).Mutate(recipe);
            }
            var name = Text(witness, "witness_id");
            Match(registry.Dispatch(packet.Case), packet, templates[Text(witness, "normal_branch")], name);
            Require(executed.Add(name), "Normal witness executed exactly once");
            if (kind == "existing-branch") continue;
            var weakening = Text(witness, "weakened_rule");
            var normal = Text(witness, "normal_branch");
            var wrong = Text(witness, "wrong_branch");
            Require(normal != wrong && WeakeningLabels.Contains(weakening, StringComparer.Ordinal),
                "Explicit accepted weakening changes the classification");
            var actual = resolver.ResolveWeakening(packet.Case, weakening);
            actual.Validate(packet.Case, resolver);
            Match(actual, packet, templates[wrong], name + " / weakened " + weakening);
            variants.Add(weakening);
            weakeningRuns++;
            if (kind == "weakened-rule") existingRuns++;
        }
        Require(recipes.SetEquals(FixedRecipes) && recipes.Count == 116 &&
            variants.SetEquals(WeakeningLabels) && variants.Count == 108 &&
            weakeningRuns == 148 && existingRuns == 32, "116 native recipes / 108 variants / 148 actual weakening executions");
        foreach (var name in Loaders(registry, catalog, directory, byId["lp04-all-sources-covered"], controls))
            Require(executed.Add(name), "Loader witness executed once");
        foreach (var name in Retained(registry, directory, retained))
            Require(executed.Add(name), "Retained witness executed once");
        var missing = spec["witnesses"]!.AsArray().Select(w => Text(w!, "witness_id"))
            .Where(id => !executed.Contains(id)).ToArray();
        Require(missing.All(id => id.StartsWith("retained:", StringComparison.Ordinal)) &&
            executed.Count == 166 - missing.Length, "Every available witness actually executed");
        var meta = 0;
        foreach (var guard in CriticalGuards)
        {
            foreach (var remove in new[] { true, false })
            {
                var changed = spec.DeepClone();
                var inventory = changed["guard_inventory"]!.AsArray();
                var item = inventory.Single(n => Text(n!, "guard_id") == guard)!;
                if (remove) inventory.Remove(item);
                else item["witness_ids"] = new JsonArray();
                Reject(() => ValidateInventory(changed), remove ? "guard-inventory" : "guard-without-witness");
                meta++;
            }
        }
        Require(meta == 256, "256 actual omission/empty-witness rejections");
        var covered = spec["guard_inventory"]!.AsArray()
            .Count(g => g!["witness_ids"]!.AsArray().All(id => executed.Contains(id!.GetValue<string>())));
        Require(covered == 128 - missing.Length, "All guard families have executed witnesses, except explicitly absent retained bindings");
        Console.WriteLine($"LP-04: 43 byte-exact canonical replays, SHA-256 {Catalog.Digest(output)}; " +
            $"{covered}/128 guard families, {executed.Count}/166 actual witnesses, {recipes.Count} native fixed recipes, " +
            $"{variants.Count} weakening variants / {weakeningRuns} executions ({existingRuns} existing, {recipes.Count} fixed), " +
            $"{meta} omission/empty-witness checks; 331 checked inputs / 735351 bytes.");
        Boundaries(registry, directory, byId, templates);
    }

    private static void CheckOracle([CallerFilePath] string source = "")
    {
        Require(Catalog.Digest(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(source)!, "lp04_expectations.py"))) ==
            "ade0f30f19d623877168b7808984ef26413c2449c85ee1ea353600fe528f6ab4", "Frozen oracle bytes (never executed)");
    }

    private static void VerifyInputs(string directory)
    {
        var inputs = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, "input-manifest.json")))!["inputs"]!.AsArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        long length = 0;
        foreach (var item in inputs)
        {
            var path = Text(item!, "path");
            Require(names.Add(path), "Manifest paths unique");
            var bytes = File.ReadAllBytes(Under(directory, path));
            Require(bytes.LongLength == item!["size_bytes"]!.GetValue<long>() &&
                Catalog.Digest(bytes) == Text(item, "sha256"), $"Frozen checked input: {path}");
            length += bytes.LongLength;
        }
        Require(names.Count == 331 && length == 735351, "All 331 frozen inputs / 735351 bytes");
    }

    private static void ValidateInventory(JsonNode spec)
    {
        Require(CriticalGuards.Length == 128 && CriticalGuards.Distinct(StringComparer.Ordinal).Count() == 128 &&
            spec["critical_guard_ids"]!.AsArray().Select(n => n!.GetValue<string>()).SequenceEqual(CriticalGuards),
            "critical-guard-list");
        var inventory = spec["guard_inventory"]!.AsArray();
        Require(inventory.Count == 128 &&
            inventory.Select(g => Text(g!, "guard_id")).ToHashSet(StringComparer.Ordinal).SetEquals(CriticalGuards),
            "guard-inventory");
        var witnesses = spec["witnesses"]!.AsArray();
        var ids = witnesses.Select(w => Text(w!, "witness_id")).ToHashSet(StringComparer.Ordinal);
        Require(ids.Count == 166 && witnesses.Count == 166, "166 unique witnesses");
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var guard in inventory)
        {
            var references = guard!["witness_ids"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
            Require(!string.IsNullOrWhiteSpace(Text(guard, "rule")), "Explicit guard rationale");
            Require(references.Length != 0, "guard-without-witness");
            Require(references.Distinct(StringComparer.Ordinal).Count() == references.Length && references.All(ids.Contains),
                "Unique known guard witnesses");
            used.UnionWith(references);
        }
        Require(used.SetEquals(ids), "No unregistered witnesses");
        Require(FixedRecipes.Length == 116 && FixedRecipes.Distinct(StringComparer.Ordinal).Count() == 116 &&
            witnesses.Where(w => Text(w!, "kind") == "fixed-in-memory").Select(w => Text(w!, "recipe"))
                .ToHashSet(StringComparer.Ordinal).SetEquals(FixedRecipes), "Independent finite recipe inventory");
        var weakening = witnesses.Where(w => Text(w!, "kind") is "weakened-rule" or "fixed-in-memory").ToArray();
        Require(WeakeningLabels.Length == 108 && WeakeningLabels.Distinct(StringComparer.Ordinal).Count() == 108 &&
            weakening.Length == 148 && weakening.Select(w => Text(w!, "weakened_rule"))
                .ToHashSet(StringComparer.Ordinal).SetEquals(WeakeningLabels), "Independent finite weakening inventory");
        Require(witnesses.Count(w => Text(w!, "kind") == "existing-branch") == 11 &&
            witnesses.Count(w => Text(w!, "kind") == "weakened-rule") == 32 &&
            witnesses.Count(w => Text(w!, "kind") == "loader") == 5 &&
            witnesses.Count(w => Text(w!, "kind") == "optional-retained") == 2, "50 unchanged named / 116 in-memory witnesses");
    }

    private sealed class Packet
    {
        internal Dictionary<string, CheckedInput> Checked { get; } = new(StringComparer.Ordinal);
        internal IReadOnlyList<PinnedInput> Inputs => Checked.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value.Identity).ToArray();
        internal ResolverCase Case => new("opaque-native-lp04-request", "LP-04", Inputs, Checked);
        internal static Packet Load(ReplayCase source, string directory)
        {
            var result = new Packet();
            foreach (var pin in source.Inputs)
            {
                var bytes = File.ReadAllBytes(Under(directory, pin.Path));
                Require(Catalog.Digest(bytes) == pin.Sha256, "Witness source checked bytes");
                result.Checked.Add(pin.Role, new(pin, bytes));
            }
            return result;
        }
        internal JsonNode Document(string role) => JsonNode.Parse(Checked[role].Content.Span)!;
        internal void Replace(string role, byte[] bytes, string? path = null)
        {
            var pin = new PinnedInput(role, path ?? (Checked.TryGetValue(role, out var old) ? old.Identity.Path : $"memory/{role}.bin"),
                Catalog.Digest(bytes));
            Checked[role] = new(pin, bytes);
        }
        internal void Put(string role, byte[] bytes) => Replace(role, bytes, $"memory/{role}.bin");
        internal void Put(string role, JsonNode node) => Put(role, JsonSerializer.SerializeToUtf8Bytes(node));
        internal void Drop(string role) => Checked.Remove(role);
        internal void Rename(string previous, string role)
        {
            var old = Checked[previous];
            Checked.Remove(previous);
            Checked.Add(role, new(old.Identity with { Role = role }, old.Content));
        }
        internal JsonNode Pin(string role) => JsonSerializer.SerializeToNode(Checked[role].Identity, Options)!;
    }

    private static void Match(CaseResult actual, Packet packet, CaseResult template, string name)
    {
        var expected = template with
        {
            Inputs = packet.Inputs,
            EvidenceReferences = packet.Inputs.Select(p => $"input:{p.Role}:{p.Sha256}").ToArray()
        };
        Require(Canonical.Write(actual.Write).SequenceEqual(Canonical.Write(expected.Write)),
            $"{name}: expected {expected.BranchId}, actual {actual.BranchId}");
    }

    private sealed class RecipePacket
    {
        private readonly Packet packet;
        private readonly Dictionary<string, JsonNode?> docs;
        private readonly JsonNode scope, coverage, admission, deps, assets, reviews, mapping;
        private readonly Dictionary<string, string> badPacket = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> badAdmission = new(StringComparer.Ordinal);
        private static readonly string Zeros = new('0', 64);
        internal RecipePacket(Packet packet)
        {
            this.packet = packet;
            docs = Words("assessment notice-coverage admission dependency-inventory asset-inventory obligation-reviews notice-mapping")
                .ToDictionary(role => role, role => packet.Checked.ContainsKey(role) ? packet.Document(role) : null, StringComparer.Ordinal);
            scope = docs["assessment"]!;
            coverage = docs["notice-coverage"]!;
            admission = docs["admission"]!;
            deps = docs["dependency-inventory"]!;
            assets = docs["asset-inventory"]!;
            reviews = docs["obligation-reviews"]!;
            mapping = docs["notice-mapping"]!;
        }
        internal JsonNode Doc(string role) => docs[role]!;
        internal void Mutate(string recipe)
        {
            Require(FixedRecipes.Contains(recipe, StringComparer.Ordinal), "Known fixed recipe");
            var firstReview = Array(reviews, "reviews").FirstOrDefault();
            var assetReview = Array(reviews, "reviews").FirstOrDefault(r => Text(r!, "subject").StartsWith("asset:", StringComparison.Ordinal));
            var notice = Array(mapping, "mappings").FirstOrDefault(m => Text(m!, "part") == "notice");
            var firstMap = Array(mapping, "mappings").FirstOrDefault();
            switch (recipe)
            {
                case "omitted-empty-reviews": docs["obligation-reviews"] = null; packet.Drop("obligation-reviews"); break;
                case "packet-root": coverage["root"]!["version"] = "9.0.0"; break;
                case "admission-root": admission["root"]!["version"] = "9.0.0"; break;
                case "dependency-root": deps["root"]!["version"] = "9.0.0"; break;
                case "asset-root": assets["root"]!["version"] = "9.0.0"; break;
                case "observed-root-id":
                    foreach (var doc in new[] { scope, coverage, admission, deps, assets }) doc["root"]!["id"] = "sample.different";
                    break;
                case "admission-scope": badAdmission["assessment_sha256"] = Zeros; break;
                case "admission-digest": badPacket["admission_sha256"] = Zeros; break;
                case "admitted-dependencies": case "admitted-assets": case "admitted-reviews": case "admitted-mapping":
                    badAdmission[recipe["admitted-".Length..]] = Zeros; break;
                case "packet-assets": case "packet-reviews": case "packet-mapping":
                    badPacket[recipe["packet-".Length..]] = Zeros; break;
                case "asset-capture-unavailable": admission["assets"]!["state"] = "unavailable"; break;
                case "target-set": deps["targets"]![1]!["tfm"] = "net10.0"; break;
                case "duplicate-target": deps["targets"]![1]!["tfm"] = Text(deps["targets"]![0]!, "tfm"); break;
                case "duplicate-node": Duplicate(Array(deps["targets"]![0]!, "nodes"), Array(deps["targets"]![0]!, "nodes").Last()!); break;
                case "duplicate-direct": Repeat(Array(deps["targets"]![0]!, "direct")); break;
                case "duplicate-adjacency": Repeat(Array(deps["targets"]![0]!["nodes"]![0]!, "dependencies")); break;
                case "edge-count-discrepancy": break;
                case "edge-limit": case "aggregate-byte-limit":
                    var count = recipe == "edge-limit" ? 16 : 9;
                    var payload = recipe == "aggregate-byte-limit" ? Enumerable.Repeat((byte)'x', 7700000).ToArray() : [];
                    ConfigureGraph(count, recipe == "edge-limit" ? 8 : 0, payload);
                    break;
                case "dangling-direct": Array(deps["targets"]![0]!, "direct").Add("sample.missing/1.0.0"); break;
                case "dangling-transitive": Array(deps["targets"]![0]!["nodes"]![0]!, "dependencies").Add("sample.missing/1.0.0"); break;
                case "orphan-node": deps["targets"]![0]!["nodes"]![0]!["dependencies"] = new JsonArray(); break;
                case "competing-versions":
                    var competing = deps["targets"]![1]!["nodes"]![0]!;
                    Duplicate(Array(deps["targets"]![0]!, "nodes"), competing);
                    Array(deps["targets"]![0]!, "direct").Add(Text(competing, "key"));
                    foreach (var review in Array(reviews, "reviews")) review!["disposition"] = "obligations";
                    break;
                case "missing-package-inventory":
                    var removed = Array(deps, "packages").Last()!;
                    packet.Drop(Text(removed, "role")); RemoveSubject("dependency:" + Text(removed, "key"));
                    Array(deps, "packages").Remove(removed);
                    break;
                case "extra-package-inventory": AddDependency("sample.z", Array(deps, "packages").Count, []); break;
                case "duplicate-package-inventory": Duplicate(Array(deps, "packages"), deps["packages"]![0]!); break;
                case "duplicate-entry-inventory": Duplicate(Array(assets, "entries"), assets["entries"]![0]!); break;
                case "entry-inventory-digest": Array(assets, "entries").Single(e => Text(e!, "path") == "LICENSE.txt")!["sha256"] = Zeros; break;
                case "duplicate-asset-inventory": Duplicate(Array(assets, "assets"), assets["assets"]![0]!); break;
                case "duplicate-entry-subject": Repeat(Array(Array(assets, "entries").First(e => Text(e!, "kind") == "third-party")!, "subjects")); break;
                case "unlisted-third-party": case "missing-listed-asset": case "unknown-entry-kind":
                    var asset = assets["assets"]![0]!;
                    RemoveSubject(Text(asset, "key")); packet.Drop(Text(asset, "role"));
                    assets["assets"] = new JsonArray();
                    if (recipe == "unlisted-third-party")
                        Array(assets, "entries").First(e => Text(e!, "kind") == "third-party")!["subjects"] = new JsonArray();
                    if (recipe == "unknown-entry-kind")
                    {
                        var entry = Array(assets, "entries").Single(e => Text(e!, "path") == Text(asset, "package_entry"))!;
                        entry["kind"] = "caller-unclassified"; entry["subjects"] = new JsonArray();
                    }
                    break;
                case "first-party-with-subject":
                    Array(assets, "entries").First(e => Text(e!, "kind") == "third-party")!["kind"] = "first-party"; break;
                case "repeated-asset-location":
                    RootEntries(additions: [("wwwroot/aa.js", packet.Checked["asset-000"].Content.ToArray())]);
                    var extra = Array(assets, "entries").Single(e => Text(e!, "path") == "wwwroot/aa.js")!;
                    extra["kind"] = "third-party"; extra["subjects"] = new JsonArray(Text(assets["assets"]![0]!, "key"));
                    break;
                case "dependency-key":
                    var oldKey = Text(deps["packages"]![0]!, "key");
                    const string newKey = "sample.aaa/1.0.0";
                    deps["packages"]![0]!["key"] = newKey;
                    foreach (var target in Array(deps, "targets"))
                    {
                        ReplaceStrings(Array(target!, "direct"), oldKey, newKey);
                        foreach (var node in Array(target!, "nodes"))
                        {
                            if (Text(node!, "key") == oldKey) node!["key"] = newKey;
                            ReplaceStrings(Array(node!, "dependencies"), oldKey, newKey);
                        }
                    }
                    RenameSubject("dependency:" + oldKey, "dependency:" + newKey);
                    break;
                case "dependency-role": case "asset-role":
                    var item = recipe == "dependency-role" ? deps["packages"]![0]! : assets["assets"]![0]!;
                    var oldRole = Text(item, "role");
                    var role = oldRole.Split('-')[0] + "-009";
                    packet.Rename(oldRole, role); item["role"] = role;
                    break;
                case "dependency-duplicate": case "dependency-alias": case "dependency-unsafe": case "dependency-digest":
                    DependencyArchive(recipe["dependency-".Length..]); break;
                case "observed-dependency-id": DependencyArchive("identity-id"); break;
                case "asset-key":
                    var before = Text(assets["assets"]![0]!, "key");
                    const string after = "vendor:widget";
                    assets["assets"]![0]!["key"] = after;
                    foreach (var entry in Array(assets, "entries")) ReplaceStrings(Array(entry!, "subjects"), before, after);
                    RenameSubject(before, after);
                    break;
                case "asset-entry":
                    RootEntries(additions: [("wwwroot/other.js", packet.Checked["asset-000"].Content.ToArray())]);
                    assets["assets"]![0]!["package_entry"] = "wwwroot/other.js";
                    break;
                case "asset-checked-digest": packet.Put("asset-000", "Internally rehashed checked asset with the wrong source bytes.\n"u8.ToArray()); break;
                case "duplicate-review": Duplicate(Array(reviews, "reviews"), firstReview!); break;
                case "extra-review": case "out-of-version-review":
                    var extraReview = firstReview!.DeepClone();
                    extraReview["subject"] = recipe == "extra-review" ? "dependency:sample.z/1.0.0" : "dependency:sample.a/9.0.0";
                    Array(reviews, "reviews").Add(extraReview);
                    break;
                case "missing-one-review":
                    var subject = Text(reviews["reviews"]![1]!, "subject");
                    Array(reviews, "reviews").RemoveAt(1);
                    RemoveWhere(Array(mapping, "mappings"), m => Text(m, "subject") == subject);
                    break;
                case "review-state": admission["reviews"]!["state"] = "unavailable"; break;
                case "no-obligations-missing-basis": packet.Drop(Text(firstReview!["basis"]!, "role")); break;
                case "entry-reference-path": firstMap!["entry"]!["path"] = "references/different-retained-path.txt"; break;
                case "basis-reference-path": firstReview!["basis"]!["path"] = "references/different-retained-path.txt"; break;
                case "part-reference-path": firstReview!["obligations"]![0]!["parts"]![0]!["evidence"]!["path"] = "references/different-retained-path.txt"; break;
                case "basis-reference-digest": firstReview!["basis"]!["sha256"] = Zeros; break;
                case "part-reference-digest": firstReview!["obligations"]![0]!["parts"]![0]!["evidence"]!["sha256"] = Zeros; break;
                case "entry-reference-role": packet.Rename("entry-000", "entry-999"); break;
                case "unsupported-only": assetReview!["disposition"] = "unsupported"; break;
                case "unresolved-only": case "reviews-before-gap": assetReview!["disposition"] = "unresolved"; break;
                case "unknown-review-disposition": firstReview!["disposition"] = "caller-asserted-none"; break;
                case "owner-fact-missing": assetReview!["owner_fact"] = null; break;
                case "owner-basis-missing": packet.Drop(Text(assetReview!["basis"]!, "role")); break;
                case "owner-with-obligations":
                    assetReview!["disposition"] = "owner-private";
                    assetReview["owner_fact"] = "Only the owner retains the necessary private agreement.";
                    break;
                case "owner-fact-without-disposition": assetReview!["owner_fact"] = "Private fact attached to the wrong disposition."; break;
                case "owner-disposition": break;
                case "none-with-obligations": firstReview!["disposition"] = "no-obligations"; break;
                case "required-without-obligations": firstReview!["disposition"] = "obligations"; break;
                case "duplicate-obligation": Duplicate(Array(firstReview!, "obligations"), firstReview!["obligations"]![0]!); break;
                case "empty-obligation-parts":
                    var obligation = firstReview!["obligations"]![0]!;
                    obligation["parts"] = new JsonArray();
                    RemoveWhere(Array(mapping, "mappings"), m => Text(m, "subject") == Text(firstReview, "subject") &&
                        Text(m, "obligation") == Text(obligation, "id"));
                    break;
                case "duplicate-part": Duplicate(Array(firstReview!["obligations"]![0]!, "parts"), firstReview!["obligations"]![0]!["parts"]![0]!); break;
                case "missing-part-bytes": packet.Drop(Text(firstReview!["obligations"]![0]!["parts"]![0]!["evidence"]!, "role")); break;
                case "duplicate-mapping": Duplicate(Array(mapping, "mappings"), firstMap!); break;
                case "extra-mapping-part": case "extra-mapping-obligation": case "out-of-inventory-mapping": case "out-of-version-mapping":
                    var extraMap = firstMap!.DeepClone();
                    if (recipe == "extra-mapping-part") extraMap["part"] = "unknown";
                    else if (recipe == "extra-mapping-obligation") extraMap["obligation"] = "unknown";
                    else extraMap["subject"] = recipe == "out-of-inventory-mapping" ? "dependency:sample.z/1.0.0" : "dependency:sample.a/9.0.0";
                    Array(mapping, "mappings").Add(extraMap);
                    break;
                case "omitted-empty-mapping": docs["notice-mapping"] = null; packet.Drop("notice-mapping"); break;
                case "mapping-state": admission["mapping"]!["state"] = "unavailable"; break;
                case "missing-subject-mapping": RemoveWhere(Array(mapping, "mappings"), m => Text(m, "subject") == Text(assetReview!, "subject")); break;
                case "missing-obligation-mapping":
                    var firstObligation = firstReview!["obligations"]![0]!;
                    var part = Array(firstObligation, "parts").Last()!;
                    Array(firstObligation, "parts").Remove(part);
                    Array(firstReview, "obligations").Add(new JsonObject { ["id"] = "second", ["parts"] = new JsonArray(part) });
                    foreach (var m in Array(mapping, "mappings"))
                        if (Text(m!, "subject") == Text(firstReview, "subject") && Text(m!, "part") == Text(part, "id")) m!["obligation"] = "second";
                    RemoveWhere(Array(mapping, "mappings"), m => Text(m, "subject") == Text(firstReview, "subject") &&
                        Text(m, "obligation") == Text(firstObligation, "id"));
                    break;
                case "missing-part-mapping": Array(mapping, "mappings").Remove(notice); break;
                case "duplicate-aggregate": case "aggregate-digest": case "aggregate-path":
                    var license = ReadEntries(packet.Checked["package"].Content.ToArray()).Single(e => e.Path == "LICENSE.txt").Bytes;
                    var aggregate = new JsonObject
                    {
                        ["path"] = recipe == "aggregate-path" ? "notices/not-packaged.txt" : "LICENSE.txt",
                        ["sha256"] = recipe == "aggregate-digest" ? Zeros : Catalog.Digest(license)
                    };
                    admission["mapping"]!["aggregates"] = recipe == "duplicate-aggregate"
                        ? new JsonArray(aggregate, aggregate.DeepClone()) : new JsonArray(aggregate);
                    break;
                case "absent-present-path": notice!["mode"] = "absent"; notice["entry"] = null; notice["offset"] = 0; notice["length"] = 0; break;
                case "absent-entry-reference": notice!["entry"] = firstMap!["entry"]!.DeepClone(); break;
                case "absent-offset": notice!["offset"] = 1; break;
                case "absent-length": notice!["length"] = 1; break;
                case "missing-entry-reference": notice!["entry"] = null; break;
                case "missing-packaged-entry": notice!["package_entry"] = "notices/not-packaged.txt"; break;
                case "missing-entry-bytes": packet.Drop(Text(notice!["entry"]!, "role")); break;
                case "entry-offset": notice!["offset"] = 1; break;
                case "entry-length": notice!["length"] = notice["length"]!.GetValue<int>() - 1; break;
                case "aggregate-offset": firstMap!["offset"] = firstMap["offset"]!.GetValue<int>() + 1; break;
                case "aggregate-length": firstMap!["length"] = firstMap["length"]!.GetValue<int>() - 1; break;
                case "partial-exact": notice!["mode"] = "partial"; break;
                case "partial-unrelated": case "partial-empty":
                    RootEntries(new Dictionary<string, byte[]> { [Text(notice!, "package_entry")] = recipe == "partial-empty" ? [] : "UNRELATED"u8.ToArray() });
                    notice!["mode"] = "partial";
                    break;
                case "extra-evidence": packet.Put("reference-999", "Unused checked evidence must not be silently ignored.\n"u8.ToArray()); break;
                case "schema-version": coverage["schema_version"] = 2; break;
                case "packet-protocol": coverage["protocol"] = "caller-asserted-notice-v1"; break;
                case "capture-profile": admission["reviews"]!["profile"] = "caller-asserted/1"; break;
                case "empty-assessment": scope["target_frameworks"] = new JsonArray(); deps["targets"] = new JsonArray(); break;
                case "duplicate-assessment":
                    scope["target_frameworks"] = new JsonArray("net8.0", "net8.0"); deps["targets"]![1]!["tfm"] = "net8.0"; break;
                case "partial-asset-scope": scope["asset_scope"] = "selected-runtime-files-only"; break;
                case "json-byte-limit": break;
                case "archive-byte-limit": RootEntries(additions: [("padding.dat", Enumerable.Repeat((byte)'x', 8 * 1024 * 1024).ToArray())]); break;
                case "asset-byte-limit":
                    var data = Enumerable.Repeat((byte)'x', 1024 * 1024 + 1).ToArray();
                    RootEntries(new Dictionary<string, byte[]> { [Text(assets["assets"]![0]!, "package_entry")] = data });
                    packet.Put("asset-000", data);
                    assets["assets"]![0]!["sha256"] = Catalog.Digest(data); assetReview!["sha256"] = Catalog.Digest(data);
                    break;
                case "root-entry-limit": RootEntries(additions: Enumerable.Range(0, 127).Select(i => ($"padding-{i:000}.txt", "x"u8.ToArray()))); break;
                default: throw new InvalidDataException("Unimplemented LP-04 fixed recipe: " + recipe);
            }
            RepairCounts();
            // Deliberately break the count AFTER repair, so the accepted discrimination remains observable.
            if (recipe == "edge-count-discrepancy") deps["targets"]![0]!["edge_count"] = deps["targets"]![0]!["edge_count"]!.GetValue<int>() + 1;
            Prune(recipe is "extra-evidence" or "entry-reference-role");
            Flush(recipe == "json-byte-limit" ? 65537 : null);
        }

        internal void RepairCounts()
        {
            foreach (var target in Array(deps, "targets"))
            {
                var nodes = Array(target!, "nodes").GroupBy(n => Text(n!, "key"), StringComparer.Ordinal).Select(g => g.Last()!).ToArray();
                target!["node_count"] = nodes.Length;
                target["edge_count"] = Array(target, "direct").Count + nodes.Sum(n => Array(n, "dependencies").Count);
            }
        }

        internal void ConfigureGraph(int count, int adjacency, byte[] payload)
        {
            foreach (var item in Array(deps, "packages").ToArray())
            {
                packet.Drop(Text(item!, "role"));
                RemoveSubject("dependency:" + Text(item!, "key"));
            }
            deps["packages"] = new JsonArray();
            var keys = Enumerable.Range(0, count).Select(i => AddDependency($"graph.node{i:00}", i, payload)).ToArray();
            foreach (var target in Array(deps, "targets"))
            {
                target!["direct"] = Strings(keys);
                target["nodes"] = new JsonArray(keys.Select(key => (JsonNode)new JsonObject
                {
                    ["key"] = key, ["dependencies"] = Strings(keys.Take(adjacency))
                }).ToArray());
            }
            RepairCounts();
        }

        internal void SetAsset(byte[] data)
        {
            var asset = assets["assets"]![0]!;
            RootEntries(new Dictionary<string, byte[]> { [Text(asset, "package_entry")] = data });
            packet.Put(Text(asset, "role"), data);
            asset["sha256"] = Catalog.Digest(data);
            Array(reviews, "reviews").Single(r => Text(r!, "subject") == Text(asset, "key"))!["sha256"] = Catalog.Digest(data);
        }

        internal void ResizeDependencyPayload(int index, int size)
        {
            var item = deps["packages"]![index]!;
            var role = Text(item, "role");
            var entries = ReadEntries(packet.Checked[role].Content.ToArray())
                .Select(e => e.Path == "payload.dat" ? (e.Path, Enumerable.Repeat((byte)'x', size).ToArray()) : e).ToList();
            var raw = WriteZip(entries);
            packet.Put(role, raw);
            item["identity"]!["sha256"] = Catalog.Digest(raw);
            Array(reviews, "reviews").Single(r => Text(r!, "subject") == "dependency:" + Text(item, "key"))!["sha256"] = Catalog.Digest(raw);
        }

        internal void SetRawRoot(byte[] raw)
        {
            packet.Put("package", raw);
            foreach (var doc in new[] { scope, coverage, admission, deps, assets }) doc["root"]!["sha256"] = Catalog.Digest(raw);
        }

        internal void Relocate(Func<string, string> path)
        {
            foreach (var role in packet.Checked.Keys.ToArray())
            {
                var input = packet.Checked[role];
                packet.Checked[role] = input with { Identity = input.Identity with { Path = path(role) } };
            }
            foreach (var review in Array(reviews, "reviews"))
            {
                review!["basis"] = packet.Pin(Text(review["basis"]!, "role"));
                foreach (var obligation in Array(review, "obligations"))
                    foreach (var part in Array(obligation!, "parts")) part!["evidence"] = packet.Pin(Text(part["evidence"]!, "role"));
            }
            foreach (var item in Array(mapping, "mappings"))
                if (item!["entry"] is { } entry) item["entry"] = packet.Pin(Text(entry, "role"));
        }

        internal void Flush(int? assessmentSize = null)
        {
            foreach (var role in DocumentRoles.Values.Prepend("assessment"))
            {
                if (docs[role] is not { } doc) continue;
                var bytes = JsonSerializer.SerializeToUtf8Bytes(doc);
                if (role == "assessment" && assessmentSize is { } size)
                    bytes = [.. bytes, .. Enumerable.Repeat((byte)' ', size - bytes.Length)];
                packet.Put(role, bytes);
            }
            admission["assessment_sha256"] = packet.Checked["assessment"].Identity.Sha256;
            foreach (var (label, role) in DocumentRoles)
                if (admission[label] is { } component)
                    component["sha256"] = packet.Checked.TryGetValue(role, out var input) ? input.Identity.Sha256 : null;
            foreach (var (key, value) in badAdmission)
                if (key == "assessment_sha256") admission[key] = value;
                else admission[key]!["sha256"] = value;
            packet.Put("admission", admission);
            coverage["assessment_sha256"] = packet.Checked["assessment"].Identity.Sha256;
            coverage["admission_sha256"] = packet.Checked["admission"].Identity.Sha256;
            foreach (var (label, role) in DocumentRoles) coverage[label] = packet.Checked.TryGetValue(role, out var input) ? input.Identity.Sha256 : null;
            foreach (var (key, value) in badPacket) coverage[key] = value;
            packet.Put("notice-coverage", coverage);
        }

        internal void Prune(bool keepUnused = false)
        {
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            if (docs["obligation-reviews"] is not null)
                foreach (var review in Array(reviews, "reviews"))
                {
                    referenced.Add(Text(review!["basis"]!, "role"));
                    foreach (var obligation in Array(review!, "obligations"))
                        foreach (var part in Array(obligation!, "parts")) referenced.Add(Text(part!["evidence"]!, "role"));
                }
            if (docs["notice-mapping"] is not null)
                foreach (var item in Array(mapping, "mappings"))
                    if (item!["entry"] is { } entry) referenced.Add(Text(entry, "role"));
            if (!keepUnused)
                foreach (var role in packet.Checked.Keys.Where(r => r.StartsWith("entry-", StringComparison.Ordinal) ||
                    r.StartsWith("reference-", StringComparison.Ordinal)).ToArray())
                    if (!referenced.Contains(role)) packet.Drop(role);
        }

        internal void Evidence(string role, byte[] data)
        {
            packet.Put(role, data);
            foreach (var review in Array(reviews, "reviews"))
            {
                if (Text(review!["basis"]!, "role") == role) review["basis"] = packet.Pin(role);
                foreach (var obligation in Array(review!, "obligations"))
                    foreach (var part in Array(obligation!, "parts"))
                        if (Text(part!["evidence"]!, "role") == role) part["evidence"] = packet.Pin(role);
            }
            foreach (var item in Array(mapping, "mappings"))
                if (item!["entry"] is { } entry && Text(entry, "role") == role) item["entry"] = packet.Pin(role);
        }

        internal void RootEntries(Dictionary<string, byte[]>? changes = null, IEnumerable<(string Path, byte[] Bytes)>? additions = null,
            Func<List<(string Path, byte[] Bytes)>, List<(string Path, byte[] Bytes)>>? transform = null)
        {
            var original = ReadEntries(packet.Checked["package"].Content.ToArray());
            var root = scope["root"]!;
            var id = Text(root, "id");
            var entries = original.Where(e => e.Path != id + ".nuspec")
                .Select(e => (e.Path, changes is not null && changes.TryGetValue(e.Path, out var data) ? data : e.Bytes)).ToList();
            if (additions is not null) entries.AddRange(additions);
            var allEntries = PackageEntries(id, Text(root, "version"), entries);
            if (transform is not null) allEntries = transform(allEntries);
            var raw = WriteZip(allEntries);
            packet.Put("package", raw);
            var identity = new JsonObject { ["id"] = id, ["version"] = Text(root, "version"), ["sha256"] = Catalog.Digest(raw) };
            foreach (var doc in new[] { scope, coverage, admission, deps, assets }) doc["root"] = identity.DeepClone();
            var previous = Array(assets, "entries").ToDictionary(e => Text(e!, "path"), e => e!, StringComparer.Ordinal);
            assets["entries"] = new JsonArray(allEntries.OrderBy(e => e.Path, StringComparer.Ordinal).Select(e => (JsonNode)new JsonObject
            {
                ["path"] = e.Path, ["sha256"] = Catalog.Digest(e.Bytes),
                ["kind"] = previous.TryGetValue(e.Path, out var old) ? Text(old, "kind") : "packaging",
                ["subjects"] = previous.TryGetValue(e.Path, out old) ? old["subjects"]!.DeepClone() : new JsonArray()
            }).ToArray());
            if (changes is null) return;
            foreach (var (path, data) in changes)
            {
                var roles = Array(mapping, "mappings").Where(m => Text(m!, "package_entry") == path && m!["entry"] is not null)
                    .Select(m => Text(m!["entry"]!, "role")).Distinct(StringComparer.Ordinal).ToArray();
                foreach (var role in roles) Evidence(role, data);
                foreach (var item in Array(mapping, "mappings"))
                    if (Text(item!, "package_entry") == path && Text(item!, "mode") != "aggregate") item!["length"] = data.Length;
            }
        }

        private string AddDependency(string id, int index, byte[] payload)
        {
            var part = Array(reviews, "reviews").SelectMany(r => Array(r!, "obligations"))
                .SelectMany(o => Array(o!, "parts")).First(p => Text(p!, "kind") == "license-text")!;
            var grant = packet.Checked[Text(part["evidence"]!, "role")].Content.ToArray();
            var raw = WriteZip(PackageEntries(id, "1.0.0", [("LICENSE.txt", grant), ("payload.dat", payload)]));
            var role = $"dependency-{index:000}";
            packet.Put(role, raw);
            var key = id + "/1.0.0";
            Array(deps, "packages").Add(new JsonObject
            {
                ["key"] = key, ["role"] = role,
                ["identity"] = new JsonObject { ["id"] = id, ["version"] = "1.0.0", ["sha256"] = Catalog.Digest(raw) }
            });
            var subject = "dependency:" + key;
            var basis = $"reference-{900 + index:000}";
            packet.Put(basis, Encoding.ASCII.GetBytes($"Fictional retained upstream terms and admitted complete obligation review for {subject}.\n"));
            Array(reviews, "reviews").Add(new JsonObject
            {
                ["subject"] = subject, ["sha256"] = Catalog.Digest(raw), ["disposition"] = "obligations",
                ["basis"] = packet.Pin(basis), ["owner_fact"] = null,
                ["obligations"] = new JsonArray(new JsonObject { ["id"] = "preservation", ["parts"] = new JsonArray(part.DeepClone()) })
            });
            var template = Array(mapping, "mappings").First(m => Text(m!, "part") == Text(part, "id"))!.DeepClone();
            template["subject"] = subject; template["obligation"] = "preservation";
            Array(mapping, "mappings").Add(template);
            return key;
        }

        private void DependencyArchive(string kind)
        {
            var item = deps["packages"]![0]!;
            var identity = item["identity"]!;
            var entries = ReadEntries(packet.Checked[Text(item, "role")].Content.ToArray())
                .Where(e => e.Path != Text(identity, "id") + ".nuspec").ToList();
            switch (kind)
            {
                case "duplicate": entries.Add(("LICENSE.txt", entries.Single(e => e.Path == "LICENSE.txt").Bytes)); break;
                case "alias": entries.Add(("license.txt", "Fictional alias.\n"u8.ToArray())); break;
                case "unsafe": entries.Add(("../unsafe.txt", "Fictional unsafe path.\n"u8.ToArray())); break;
                default: entries.Add(("additional.txt", "Different checked archive, same observed identity.\n"u8.ToArray())); break;
            }
            var raw = WriteZip(PackageEntries(kind == "identity-id" ? "sample.different" : Text(identity, "id"), Text(identity, "version"), entries));
            packet.Put(Text(item, "role"), raw);
            if (kind == "digest") return;
            identity["sha256"] = Catalog.Digest(raw);
            Array(reviews, "reviews").Single(r => Text(r!, "subject") == "dependency:" + Text(item, "key"))!["sha256"] = Catalog.Digest(raw);
        }

        private void RemoveSubject(string subject)
        {
            RemoveWhere(Array(reviews, "reviews"), r => Text(r, "subject") == subject);
            RemoveWhere(Array(mapping, "mappings"), m => Text(m, "subject") == subject);
        }
        private void RenameSubject(string before, string after)
        {
            foreach (var item in Array(reviews, "reviews").Concat(Array(mapping, "mappings")))
                if (Text(item!, "subject") == before) item!["subject"] = after;
        }
    }

    private static List<(string Path, byte[] Bytes)> PackageEntries(string id, string version, IEnumerable<(string Path, byte[] Bytes)> entries)
    {
        var nuspec = Encoding.ASCII.GetBytes($"<?xml version=\"1.0\" encoding=\"utf-8\"?><package><metadata><id>{id}</id>" +
            $"<version>{version}</version><authors>Fictional</authors><description>Synthetic only</description>" +
            "<license type=\"expression\">MIT</license></metadata></package>");
        return [(id + ".nuspec", nuspec), .. entries];
    }
    private static byte[] WriteZip(IEnumerable<(string Path, byte[] Bytes)> entries, int attributes = unchecked((int)0x81a40000))
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            foreach (var (path, bytes) in entries)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.NoCompression);
                entry.LastWriteTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
                entry.ExternalAttributes = attributes;
                using var output = entry.Open();
                output.Write(bytes);
            }
        return buffer.ToArray();
    }
    private static List<(string Path, byte[] Bytes)> ReadEntries(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes));
        return zip.Entries.Select(entry =>
        {
            using var input = entry.Open();
            using var output = new MemoryStream();
            input.CopyTo(output);
            return (entry.FullName, output.ToArray());
        }).ToList();
    }

    private static IEnumerable<string> Loaders(Registry registry, Catalog catalog, string directory, ReplayCase positive, JsonArray controls)
    {
        var handler = new RejectDispatch();
        var guarded = new Registry(catalog, registry.Rows.Select(row => row.Id == "LP-04"
            ? new RegistryEntry(row.Id, "implemented", handler.ResolverId, handler.Version)
            : new RegistryEntry(row.Id, "tbd")), [handler]);
        var scratch = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".lp04-loaders-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(scratch);
        var result = new List<string>();
        try
        {
            foreach (var pin in positive.Inputs)
            {
                var target = Under(scratch, pin.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var bytes = File.ReadAllBytes(Under(directory, pin.Path));
                Require(Catalog.Digest(bytes) == pin.Sha256, "Loader source copy checked");
                File.WriteAllBytes(target, bytes);
            }
            foreach (var control in controls)
            {
                var test = JsonSerializer.SerializeToNode(positive, Options)!;
                test["inputs"] = control!["inputs"]!.DeepClone();
                var path = Path.Combine(scratch, "loader.json");
                File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new JsonObject
                {
                    ["schema_version"] = 1, ["cases"] = new JsonArray(test)
                }));
                Exception? rejected = null;
                try { Replay.Run(path, guarded); }
                catch (Exception error) when (error is InvalidDataException or JsonException or FileNotFoundException) { rejected = error; }
                Require(Text(control, "expected_rejection") switch
                {
                    "duplicate-role" or "invalid-relative-path" => rejected is InvalidDataException &&
                        rejected.Message.Contains("Conflicting or invalid pinned inputs", StringComparison.Ordinal),
                    "missing-file" => rejected is FileNotFoundException,
                    "input-digest-mismatch" => rejected is InvalidDataException &&
                        rejected.Message.Contains("digest-mismatched input", StringComparison.Ordinal),
                    "invalid-fields" => rejected is JsonException,
                    _ => false
                }, "Loader rejects the exact accepted failure class: " + Text(control, "case_id"));
                Require(handler.Calls == 0, "Loader rejection occurs before production dispatch");
                result.Add("loader:" + Text(control, "guard_id"));
            }
        }
        finally { Directory.Delete(scratch, true); }
        Require(result.Count == 5 && result.Distinct(StringComparer.Ordinal).Count() == 5, "Five actual independent loader rejections");
        return result;
    }

    private sealed class RejectDispatch : IDeterministicResolver
    {
        public int Calls { get; private set; }
        public string RowId => "LP-04";
        public string ResolverId => "lp04-notice-coverage";
        public string Version => "1.0.0";
        public CaseResult Resolve(ResolverCase input)
        {
            Calls++;
            throw new InvalidOperationException("Rejected LP-04 input reached dispatch.");
        }
    }

    private static IEnumerable<string> Retained(Registry registry, string directory, CaseSuite suite)
    {
        var vendors = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, "retained-inputs.json")))!["vendors"]!.AsArray();
        Dictionary<string, byte[]> ReadBindings(Dictionary<string, string> bindings)
        {
            var allowed = vendors.Select(v => Text(v!, "binding")).ToHashSet(StringComparer.Ordinal);
            Require(bindings.Keys.All(allowed.Contains), "unknown-retained-binding");
            Require(bindings.Count == 2 && allowed.SetEquals(bindings.Keys), "both-retained-bindings-required");
            var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var vendor in vendors)
            {
                var path = bindings[Text(vendor!, "binding")];
                Require(File.Exists(path), "retained-file-missing");
                Require(new FileInfo(path).Length == vendor!["size_bytes"]!.GetValue<long>(), "retained-size-mismatch");
                var bytes = File.ReadAllBytes(path);
                Require(bytes.LongLength == vendor["size_bytes"]!.GetValue<long>(), "retained-size-mismatch");
                Require(Catalog.Digest(bytes) == Text(vendor, "sha256"), "retained-digest-mismatch");
                result.Add(Text(vendor, "vendor"), bytes);
            }
            return result;
        }
        var scratch = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".lp04-bindings-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(scratch);
        var bad = Path.Combine(scratch, "wrong.nupkg");
        var negatives = 0;
        void Negative(Dictionary<string, string> bindings, string expected)
        {
            Reject(() => ReadBindings(bindings), expected);
            negatives++;
        }
        try
        {
            Negative(new(), "both-retained-bindings-required");
            Negative(new() { ["LP04_SYNCFUSION_NUPKG"] = bad }, "both-retained-bindings-required");
            Negative(new() { ["LP04_MISSPELLED_NUPKG"] = bad }, "unknown-retained-binding");
            var both = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LP04_SYNCFUSION_NUPKG"] = bad, ["LP04_INFRAGISTICS_NUPKG"] = bad
            };
            Negative(both, "retained-file-missing");
            File.WriteAllBytes(bad, [0]);
            Negative(both, "retained-size-mismatch");
            File.WriteAllBytes(bad, new byte[4912251]);
            Negative(both, "retained-digest-mismatch");
        }
        finally { Directory.Delete(scratch, true); }
        Require(negatives == 6, "Six retained binding rejection classes executed");
        var bindings = Environment.GetEnvironmentVariables().Keys.OfType<string>()
            .Where(name => name.StartsWith("LP04_", StringComparison.Ordinal))
            .ToDictionary(name => name, name => Environment.GetEnvironmentVariable(name)!, StringComparer.Ordinal);
        if (bindings.Count == 0)
        {
            Console.WriteLine("LP-04 retained: six binding negatives passed; actual archives NOT RUN (both optional local bindings absent); no download.");
            return [];
        }
        var buffers = ReadBindings(bindings);
        var results = new List<(string Id, CaseResult Result)>();
        var executed = new List<string>();
        foreach (var vendor in vendors)
        {
            var name = Text(vendor!, "vendor");
            var bytes = buffers[name];
            using var zip = new ZipArchive(new MemoryStream(bytes));
            var nuspec = zip.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            Require(nuspec.Length <= 1024 * 1024, "Actual retained nuspec identity read is bounded");
            byte[] xmlBytes;
            using (var stream = nuspec.Open())
            using (var output = new MemoryStream())
            {
                stream.CopyTo(output);
                xmlBytes = output.ToArray();
            }
            Require(Catalog.Digest(xmlBytes) == (name == "syncfusion"
                ? "e9a55efdfc3385c1df49206839689f3f01c0bf5731b6130f05a601632a77acb7"
                : "8399959e38391faa853f874f6d4c2d10a2724f3e978af5fc79fbd46a7a74833c"), "Exact retained nuspec digest");
            using var xmlStream = new MemoryStream(xmlBytes);
            using var reader = XmlReader.Create(xmlStream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024
            });
            var xml = XDocument.Load(reader);
            var metadata = xml.Root!.Elements().Single(e => e.Name.LocalName == "metadata");
            Require(metadata.Element(metadata.Name.Namespace + "id")!.Value.ToLowerInvariant() == Text(vendor!, "package_id") &&
                metadata.Element(metadata.Name.Namespace + "version")!.Value == Text(vendor!, "version"), "Actual root identity from the same checked archive buffer");
            var source = suite.Cases.Single(c => c.Inputs.Single().Sha256 == Text(vendor!, "sha256"));
            Require(source.Inputs.Count == 1 && source.Inputs.Single().Role == "package", "Retained expectation does not manufacture scope");
            var packet = new Packet();
            var pin = source.Inputs.Single();
            packet.Checked.Add("package", new(pin, bytes));
            var actual = registry.Dispatch(packet.Case);
            Match(actual, packet, source.Expected, source.CaseId);
            Require(actual.BranchId == "scope-missing" && actual.Decision == "escalated" &&
                actual.Applicability == "unknown" && actual.Finding is null,
                "Actual retained roots remain scope-missing/not tested, never verified or gap");
            results.Add((source.CaseId, actual));
            executed.Add("retained:" + name);
        }
        var replay = Canonical.Write(writer =>
        {
            writer.WriteStartObject(); writer.WriteNumber("schema_version", 1);
            writer.WritePropertyName("cases"); writer.WriteStartArray();
            foreach (var (id, result) in results)
            {
                writer.WriteStartObject(); writer.WriteString("case_id", id);
                writer.WritePropertyName("result"); result.Write(writer); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteEndObject();
        });
        Require(executed.Count == 2, "Two actual retained checks executed together");
        Console.WriteLine($"LP-04 retained: 2 actual exact roots/nuspec identities, scope-missing/not tested; six binding negatives; SHA-256 {Catalog.Digest(replay)}.");
        return executed;
    }

    private static void Boundaries(Registry registry, string directory, Dictionary<string, ReplayCase> cases,
        Dictionary<string, CaseResult> templates)
    {
        const string positive = "notice-coverage-satisfied";
        const string empty = "no-applicable-surface";
        var checks = 0;
        var labels = new HashSet<string>(StringComparer.Ordinal);
        var failures = new List<string>();
        void Check(string name, Action<Packet> mutate, string branch = "input-invalid", string source = "all-sources-covered")
        {
            Require(labels.Add(name), "Unique runtime boundary name");
            try
            {
                var packet = Packet.Load(cases["lp04-" + source], directory);
                mutate(packet);
                Match(registry.Dispatch(packet.Case), packet, templates[branch], "runtime: " + name);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                failures.Add($"{name}: {error.GetType().Name}: {error.Message}");
            }
            checks++;
        }
        void Edit(Packet packet, string role, Action<JsonNode> edit)
        {
            var documents = new RecipePacket(packet);
            edit(documents.Doc(role));
            documents.Flush();
        }
        void Build(Packet packet, Action<RecipePacket> edit)
        {
            var documents = new RecipePacket(packet);
            edit(documents);
            documents.Prune();
            documents.Flush();
        }
        void RootXml(Packet packet, Func<XDocument, XDocument> edit) => Build(packet, docs => docs.RootEntries(transform: entries =>
        {
            var nuspec = entries.FindIndex(e => e.Path.EndsWith(".nuspec", StringComparison.Ordinal));
            var xml = XDocument.Parse(Encoding.UTF8.GetString(entries[nuspec].Bytes));
            entries[nuspec] = (entries[nuspec].Path, Encoding.UTF8.GetBytes(edit(xml).ToString(SaveOptions.DisableFormatting)));
            return entries;
        }));

        Check("inclusive 65536-byte JSON", p => new RecipePacket(p).Flush(65536), positive);
        Check("65537-byte JSON", p => new RecipePacket(p).Flush(65537));
        foreach (var role in Words("assessment notice-coverage admission dependency-inventory asset-inventory obligation-reviews notice-mapping"))
        {
            foreach (var raw in new[] { "null", "[]", "1", "{}", "{\"schema_version\":null}", "{\"schema_version\":true}", "{\"schema_version\":1.0}", "{\"schema_version\":1e0}" })
            {
                var branch = raw != "null" || role == "assessment" ? "input-invalid" :
                    role is "obligation-reviews" or "notice-mapping" ? "binding-conflict" : "evidence-missing";
                Check($"{role} malformed shape {raw}", p => p.Put(role, Encoding.UTF8.GetBytes(raw)), branch);
            }
            Check($"{role} BOM", p => p.Put(role, [239, 187, 191, .. p.Checked[role].Content.ToArray()]));
            Check($"{role} invalid UTF8", p => p.Put(role, [0xff]));
        }
        foreach (var raw in new[]
        {
            "{\"schema_version\":NaN}", "{\"schema_version\":Infinity}", "{\"schema_version\":1,}",
            "{\"schema_version\":1} {}", "{\"schema_version\":1,\"root\":\"\\ud800\"}",
            "{\"schema_version\":1,\"x\":[[[[[[[[null]]]]]]]]}", "{\"schema_version\":1,//comment\n\"x\":0}",
            "{\"schema_version\":1,\"schema_version\":1}"
        })
            Check("strict JSON " + raw, p => p.Put("assessment", Encoding.UTF8.GetBytes(raw)));
        Check("null root identity", p => Edit(p, "assessment", n => n["root"] = null));
        Check("null TFM", p => Edit(p, "assessment", n => n["target_frameworks"]![0] = null));
        Check("TFMs not array", p => Edit(p, "assessment", n => n["target_frameworks"] = "net8.0"));
        Check("null target", p => Edit(p, "dependency-inventory", n => n["targets"]![0] = null));
        Check("null node", p => Edit(p, "dependency-inventory", n => n["targets"]![0]!["nodes"]![0] = null));
        Check("null dependency", p => Edit(p, "dependency-inventory", n => n["packages"]![0] = null));
        Check("null asset", p => Edit(p, "asset-inventory", n => n["assets"]![0] = null));
        Check("null root entry", p => Edit(p, "asset-inventory", n => n["entries"]![0] = null));
        Check("null review", p => Edit(p, "obligation-reviews", n => n["reviews"]![0] = null));
        Check("null obligation", p => Edit(p, "obligation-reviews", n => n["reviews"]![0]!["obligations"]![0] = null));
        Check("null part", p => Edit(p, "obligation-reviews", n => n["reviews"]![0]!["obligations"]![0]!["parts"]![0] = null));
        Check("null mapping", p => Edit(p, "notice-mapping", n => n["mappings"]![0] = null));
        Check("null admission component", p => Edit(p, "admission", n => n["reviews"] = null));
        foreach (var text in new[] { "\u0131", "\u00e9", "line\nbreak", "\t", "\u007f", "", " ", new string('a', 65) })
            Check("strict ASCII token " + JsonSerializer.Serialize(text), p => Edit(p, "assessment", n => n["target_frameworks"]![0] = text));
        foreach (var version in new[] { "01.0.0", "1.0", "1.0.0-preview", "100000.0.0", "1.0.0.0", "1.0.0 " })
            Check("exact version " + version, p => Edit(p, "assessment", n => n["root"]!["version"] = version));
        Check("uppercase SHA", p => Edit(p, "assessment", n => n["root"]!["sha256"] = Text(n["root"]!, "sha256").ToUpperInvariant()));
        Check("uppercase package token", p => Edit(p, "assessment", n => n["root"]!["id"] = "Sample.Root"));
        Check("unsupported capture state", p => Edit(p, "admission", n => n["dependencies"]!["state"] = "complete"));
        Check("unsupported dependency profile", p => Edit(p, "dependency-inventory", n => n["profile"] = "caller-graph/1"));
        Check("unsupported part kind", p => Edit(p, "obligation-reviews", n => n["reviews"]![0]!["obligations"]![0]!["parts"]![0]!["kind"] = "spdx"));
        Check("unsupported mapping mode", p => Edit(p, "notice-mapping", n => n["mappings"]![0]!["mode"] = "equivalent-text"));
        Check("boolean edge count", p => Edit(p, "dependency-inventory", n => n["targets"]![0]!["edge_count"] = true));
        Check("negative node count", p => Edit(p, "dependency-inventory", n => n["targets"]![0]!["node_count"] = -1));
        Check("negative mapping offset", p => Edit(p, "notice-mapping", n => n["mappings"]![0]!["offset"] = -1));
        Check("mapping integer overflow", p => Edit(p, "notice-mapping", n => n["mappings"]![0]!["length"] = long.MaxValue));
        Check("out-of-profile mapping offset", p => Edit(p, "notice-mapping", n => n["mappings"]![0]!["offset"] = 16385));
        Check("oversized JSON string", p => Edit(p, "admission", n => n["reviews"]!["reference"] = new string('a', 1025)));

        foreach (var path in new[]
        {
            "../escape.txt", "a/../b", "/absolute.txt", "a\\b", "C:/absolute.txt", "a//b", "./a", "a/.",
            "a.", "a /b", "a/b.", "a:b", "a%2fb", "a?b", "a#b", "a/\u00e9", "CON", "con.txt",
            "a/PrN.md", "AUX", "nul.bin", "com1.txt", "COM9", "lpt1", "LPT9.txt", new string('a', 257)
        })
            Check("safe evidence path " + path, p => Edit(p, "obligation-reviews", n => n["reviews"]![0]!["basis"]!["path"] = path));
        foreach (var path in new[] { "../escape.txt", "a\\b", "CON.txt", "a.", new string('a', 257) })
            Check("safe checked identity " + path, p =>
            {
                var input = p.Checked["reference-003"];
                p.Checked["reference-003"] = input with { Identity = input.Identity with { Path = path } };
            });
        Check("unknown checked role", p => p.Put("caller-complete", "asserted"u8.ToArray()));
        Check("unbounded role index", p => p.Rename("reference-003", "reference-1000"));
        foreach (var bytes in new byte[][]
        {
            [], [239, 187, 191, (byte)'x'], [0xff], [0xc0, 0x80], [0xed, 0xa0, 0x80], [0xe2, 0x82],
            [(byte)' ', (byte)'\t', (byte)'\n'], [0x1c, 0x20], [0xc2, 0xa0]
        })
        {
            var id = Convert.ToHexString(bytes);
            Check("strict retained UTF8 " + id, p => p.Put("reference-003", bytes));
        }
        Check("reference exceeds 16384", p => p.Put("reference-003", Enumerable.Repeat((byte)'x', 16385).ToArray()));
        Check("entry exceeds 16384", p => p.Put("entry-001", Enumerable.Repeat((byte)'x', 16385).ToArray()));
        Check("asset exceeds 1MiB", p => Build(p, d => d.SetAsset(new byte[1024 * 1024 + 1])));
        Check("empty asset", p => p.Put("asset-000", []));
        Check("empty root archive", p => p.Put("package", []));
        Check("root exceeds 8MiB", p => p.Put("package", new byte[8 * 1024 * 1024 + 1]));
        Check("dependency exceeds 8MiB", p => p.Put("dependency-000", new byte[8 * 1024 * 1024 + 1]));
        Check("valid retained UTF8 is not restricted to ASCII", p => Build(p, d => d.Evidence("reference-003", Encoding.UTF8.GetBytes("Fictional retained révision.\n"))), positive);
        Check("inclusive retained reference cap", p => Build(p, d => d.Evidence("reference-003", Enumerable.Repeat((byte)'x', 16384).ToArray())), positive);
        Check("inclusive opaque asset cap", p => Build(p, d => d.SetAsset(new byte[1024 * 1024])), positive);
        Check("inclusive 128 root entries", p => Build(p, d => d.RootEntries(additions:
            Enumerable.Range(0, 126).Select(i => ($"padding-{i:000}.txt", "x"u8.ToArray())))), empty, "no-applicable-surface");
        Check("inclusive 128 graph edges", p => Build(p, d => d.ConfigureGraph(16, 7, [])), positive);
        Check("129 graph edges", p => Build(p, d =>
        {
            d.ConfigureGraph(16, 7, []);
            foreach (var t in Array(d.Doc("dependency-inventory"), "targets"))
                Array(t!["nodes"]![0]!, "dependencies").Add("graph.node07/1.0.0");
            d.RepairCounts();
        }));
        Check("inclusive 64 graph nodes", p => Build(p, d => d.ConfigureGraph(64, 0, [])), positive);
        Check("65 graph nodes", p => Build(p, d => d.ConfigureGraph(65, 0, [])));
        Check("inclusive four target frameworks", p => Build(p, d =>
        {
            foreach (var tfm in new[] { "net10.0", "net11.0" })
            {
                Array(d.Doc("assessment"), "target_frameworks").Add(tfm);
                var target = d.Doc("dependency-inventory")["targets"]![0]!.DeepClone(); target["tfm"] = tfm;
                Array(d.Doc("dependency-inventory"), "targets").Add(target);
            }
        }), positive);
        Check("five target frameworks", p => Edit(p, "assessment", n => n["target_frameworks"] = new JsonArray("a", "b", "c", "d", "e")));
        Check("inclusive supported exact version", p => Build(p, d =>
        {
            d.Doc("assessment")["root"]!["version"] = "99999.99999.99999";
            d.RootEntries();
        }), positive);
        Check("inclusive 8MiB native root archive", p => Build(p, d =>
        {
            d.RootEntries(additions: [("padding.dat", [])]);
            var remaining = 8 * 1024 * 1024 - p.Checked["package"].Content.Length;
            d.RootEntries(new Dictionary<string, byte[]> { ["padding.dat"] = new byte[remaining] });
            Require(p.Checked["package"].Content.Length == 8 * 1024 * 1024, "Native ZIP exactly at archive cap");
        }), positive);
        foreach (var excess in new[] { 0, 1 })
            Check("aggregate checked bytes 64MiB +" + excess, p =>
            {
                var d = new RecipePacket(p);
                const int payload = 7400000;
                d.ConfigureGraph(9, 0, new byte[payload]); d.Prune(); d.Flush();
                var remaining = checked((int)(64 * 1024 * 1024L - p.Checked.Values.Sum(v => (long)v.Content.Length)));
                d.ResizeDependencyPayload(0, payload + remaining + excess); d.Flush();
                Require(p.Checked.Values.Sum(v => (long)v.Content.Length) == 64 * 1024 * 1024L + excess,
                    "Exact aggregate checked byte boundary");
            }, excess == 0 ? positive : "input-invalid");

        foreach (var path in new[] { "LICENSE.txt", "license.TXT", "../unsafe.txt", "a/CON.txt", "trailing." })
            Check("native archive unsafe/duplicate/alias " + path, p => Build(p, d => d.RootEntries(additions: [(path, "x"u8.ToArray())])));
        Check("native archive symlink", p => Build(p, d =>
            d.SetRawRoot(WriteZip(ReadEntries(p.Checked["package"].Content.ToArray()), unchecked((int)0xa1ff0000)))));
        Check("native archive truncated", p => Build(p, d => d.SetRawRoot(p.Checked["package"].Content.ToArray()[..^1])));
        Check("native archive CRC corruption", p => Build(p, d =>
        {
            var bytes = p.Checked["package"].Content.ToArray();
            var marker = "Fictional"u8.ToArray();
            var offset = Enumerable.Range(0, bytes.Length - marker.Length)
                .First(i => bytes.AsSpan(i, marker.Length).SequenceEqual(marker));
            bytes[offset] ^= 1;
            d.SetRawRoot(bytes);
        }));
        Check("DTD prohibited", p => RootXml(p, xml =>
        {
            xml.AddFirst(new XDocumentType("package", null, null, "<!ENTITY x 'unsafe'>")); return xml;
        }));
        Check("duplicate nuspec identity", p => RootXml(p, xml =>
        {
            var metadata = xml.Root!.Element("metadata")!; metadata.Add(new XElement(metadata.Element("id")!)); return xml;
        }));
        Check("missing nuspec version", p => RootXml(p, xml => { xml.Root!.Element("metadata")!.Element("version")!.Remove(); return xml; }));
        Check("foreign nuspec identity", p => RootXml(p, xml =>
        {
            xml.Root!.Element("metadata")!.Element("id")!.Name = XName.Get("id", "urn:foreign"); return xml;
        }));
        Check("unsafe nuspec exact version", p => RootXml(p, xml => { xml.Root!.Element("metadata")!.Element("version")!.Value = "1.0"; return xml; }));
        Check("additional nuspec", p => Build(p, d => d.RootEntries(additions: [("another.nuspec", "<package/>"u8.ToArray())])));
        Check("missing nuspec", p => Build(p, d => d.RootEntries(transform: entries =>
            entries.Where(e => !e.Path.EndsWith(".nuspec", StringComparison.Ordinal)).ToList())));
        Check("missing package precedes malformed scope", p => { p.Drop("package"); p.Put("assessment", "null"u8.ToArray()); }, "missing-package");
        Check("missing scope precedes malformed root", p => { p.Drop("assessment"); p.Put("package", "not a zip"u8.ToArray()); }, "scope-missing");
        Check("shape precedes binding disagreement", p => Edit(p, "assessment", n =>
        {
            n["root"]!["sha256"] = new string('0', 64); n["target_frameworks"]![0] = null;
        }));
        Check("later binding precedes first missing subject", p => Edit(p, "obligation-reviews", n =>
        {
            p.Drop("dependency-000"); n["reviews"]![1]!["sha256"] = new string('0', 64);
        }), "binding-conflict");
        Check("missing review before a complete gap", p => Edit(p, "obligation-reviews", n => n["reviews"]![2]!["disposition"] = "unresolved"),
            "reviews-unresolved", "direct-absent");
        Check("owner fact before complete gap", _ => { }, "owner-evidence-required", "owner-before-gap");
        Check("missing mapping before complete gap", _ => { }, "mapping-unresolved", "unresolved-before-gap");
        Check("no path reopening", p => Build(p, d =>
        {
            d.Relocate(role => $"unopened-lp04/{role}.bin");
            Require(p.Inputs.All(pin => !File.Exists(Under(Environment.CurrentDirectory, pin.Path))), "All checked paths absent");
        }), positive);
        Check("inclusive 256-character proof path", p => Build(p, d => d.Relocate(role => role == "reference-003"
            ? new string('a', 256) : $"unopened-lp04/{role}.bin")), positive);
        Check("roles and paths rather than case ID", p =>
        {
            var expected = registry.Dispatch(p.Case);
            var renamed = registry.Dispatch(p.Case with { CaseId = "lp04-direct-absent" });
            Require(Canonical.Write(expected.Write).SequenceEqual(Canonical.Write(renamed.Write)), "Case ID never selects expected branch");
        }, positive);
        var initial = Packet.Load(cases["lp04-all-sources-covered"], directory);
        var original = registry.Dispatch(initial.Case);
        var cultures = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            foreach (var culture in new[] { "tr-TR", "fr-FR", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                Match(registry.Dispatch(initial.Case), initial, templates[positive], "Culture independent " + culture);
                checks++;
            }
        }
        finally { CultureInfo.CurrentCulture = cultures.Item1; CultureInfo.CurrentUICulture = cultures.Item2; }
        var unsorted = initial.Case with { Inputs = initial.Inputs.Reverse().ToArray() };
        var reversed = registry.Dispatch(unsorted);
        Require(reversed.EvidenceReferences.SequenceEqual(original.EvidenceReferences), "Evidence citations always ordinal role-sorted");
        Require(reversed.Inputs.SequenceEqual(unsorted.Inputs), "Result preserves caller input identities/order");
        checks++;
        foreach (var label in new[] { "", "unknown-weakening", "graph-counts " })
            Check("unrecognized weakening " + JsonSerializer.Serialize(label), p =>
            {
                CaseResult? rejected = null;
                try { rejected = new Lp04NoticeCoverageResolver().ResolveWeakening(p.Case, label); }
                catch (InvalidDataException) { }
                if (rejected is not null) Match(rejected, p, templates["input-invalid"], "Unrecognized weakening is rejected");
            }, positive);
        foreach (var variant in new[] { "bytes", "identity", "missing", "extra" })
        {
            var packet = Packet.Load(cases["lp04-all-sources-covered"], directory);
            var input = packet.Case;
            var checkedInputs = packet.Checked;
            var old = checkedInputs["package"];
            switch (variant)
            {
                case "bytes": checkedInputs["package"] = old with { Content = "tampered"u8.ToArray() }; break;
                case "identity": checkedInputs["package"] = old with { Identity = old.Identity with { Path = "different.nupkg" } }; break;
                case "missing": checkedInputs.Remove("package"); break;
                case "extra": checkedInputs["unsupported"] = old; break;
            }
            Reject(() => registry.Dispatch(input));
            checks++;
        }
        Require(failures.Count == 0, $"Runtime boundaries ({failures.Count}/{checks} failed):\n" + string.Join("\n", failures));
        Console.WriteLine($"LP-04 runtime: {checks} actual strict-parser/ASCII/UTF8/depth/enums/path/archive/nuspec/inclusive-limit/precedence/checked-buffer/culture checks passed.");
    }

    private static JsonArray Array(JsonNode node, string key) => node[key]!.AsArray();
    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());
    private static void Duplicate(JsonArray array, JsonNode item) => array.Add(item.DeepClone());
    private static void Repeat(JsonArray array)
    {
        foreach (var item in array.ToArray()) array.Add(item!.DeepClone());
    }
    private static void RemoveWhere(JsonArray array, Func<JsonNode, bool> predicate)
    {
        foreach (var item in array.Where(n => predicate(n!)).ToArray()) array.Remove(item);
    }
    private static void ReplaceStrings(JsonArray array, string before, string after)
    {
        for (var i = 0; i < array.Count; i++) if (array[i]!.GetValue<string>() == before) array[i] = after;
    }
    private static string[] Words(string value) => value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    private static string Text(JsonNode node, string key) => node[key]!.GetValue<string>();
    private static string Under(string directory, string relative) => Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException("LP-04 check failed: " + message);
    }
    private static void Reject(Action action, string? expected = null)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or JsonException or FileNotFoundException)
        {
            if (expected is not null) Require(error.Message.Contains(expected, StringComparison.Ordinal), "Wrong rejection: " + error.Message);
            return;
        }
        throw new InvalidOperationException("Expected LP-04 input rejection.");
    }
}
