using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using BlazorPackageResolver;

internal static class Lp03Checks
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    private static readonly Dictionary<string, string> FrozenHashes = new(StringComparer.Ordinal)
    {
        ["README.md"] = "6412d0e2693cea90222bf7fe099541d5ca37bf4826201b69f2b75a033ca9be42",
        ["acceptance-manifest.json"] = "907b2c7d114d56e745b63eced1176a460d50d24d6bd02cde812d54326d4b21d8",
        ["discrimination.json"] = "ab28a984d9757dc62096b3a91082134b69ab23da7cdbde6a4b8a7aa3ede41371",
        ["cases.json"] = "935bc6e80fcaf7d1a5ad1526b2ee5a2409d6041fdbccfd6fb1b47e272b1f744b",
        ["input-manifest.json"] = "04f3d542ee4ff5d544270982f21c7e9e87e73ad591d2763eeef57cfc7cf72de5",
        ["rejected-inputs.json"] = "ae7cd3af1fa9050cbd2e222d2e7efc2d455aa2f3e429dd478ec2465b71bdeadb",
        ["acceptance.json"] = "5f6a5a58c73939e7cb3f28f740acfcf26e2cfd883f0ebfb0f99d63ad1e50e255",
        ["retained-inputs.json"] = "8bd89262559999a2589267815761af820992965f336a1b4a0e9f1a5aff3e9bb6"
    };

    internal static void Run(Registry registry, Catalog catalog, string directory)
    {
        foreach (var (path, hash) in FrozenHashes)
            Require(Catalog.Digest(File.ReadAllBytes(Path.Combine(directory, path))) == hash, $"Phase A pin: {path}");
        var fixture = Path.Combine(directory, "cases.json");
        var suite = JsonSerializer.Deserialize<CaseSuite>(File.ReadAllBytes(fixture), Options)!;
        Require(suite.Cases.Count == 43, "43 accepted replays");
        var output = Replay.Run(fixture, registry);
        Require(output.SequenceEqual(Replay.Run(fixture, registry)), "Exact canonical replay determinism");
        var retainedSuite = JsonSerializer.Deserialize<CaseSuite>(
            File.ReadAllBytes(Path.Combine(directory, "acceptance.json")), Options)!;
        var templates = suite.Cases.Concat(retainedSuite.Cases).Select(c => c.Expected)
            .GroupBy(c => c.BranchId).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var byId = suite.Cases.ToDictionary(c => c.CaseId, StringComparer.Ordinal);
        var spec = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, "discrimination.json")))!;
        var witnesses = spec["witnesses"]!.AsArray();
        var executed = new HashSet<string>(StringComparer.Ordinal);
        var recipes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var witness in witnesses)
        {
            var packet = Packet.Load(byId[Text(witness!, "case_id")], directory);
            if (Text(witness!, "kind") == "fixed-in-memory")
            {
                var recipe = Text(witness!, "recipe");
                Require(recipes.Add(recipe), "Fixed recipe registered once");
                Mutate(packet, recipe);
            }
            var branch = Text(witness!, "normal_branch");
            Require(branch != Text(witness!, "wrong_branch"), "Witness distinguishes an incorrect branch");
            Match(registry, packet, templates[branch], Text(witness!, "witness_id"));
            Require(executed.Add(Text(witness!, "witness_id")), "Witness executed once");
        }
        var critical = spec["critical_guard_ids"]!.AsArray().Select(n => n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        bool InventoryValid(JsonNode candidate)
        {
            var inventory = candidate["guard_inventory"]!.AsArray();
            return inventory.Count == critical.Count &&
                inventory.Select(g => Text(g!, "guard_id")).ToHashSet(StringComparer.Ordinal).SetEquals(critical) &&
                inventory.All(g => g!["witness_ids"]!.AsArray() is { Count: > 0 } ids &&
                    ids.All(id => executed.Contains(id!.GetValue<string>())));
        }
        Require(critical.Count == 66 && executed.Count == 77 && recipes.Count == 33 &&
            InventoryValid(spec), "All 66 frozen guard families have executed production witnesses");
        var meta = 0;
        foreach (var guard in critical)
        {
            foreach (var remove in new[] { true, false })
            {
                var changed = spec.DeepClone();
                var inventory = changed["guard_inventory"]!.AsArray();
                var item = inventory.Single(n => Text(n!, "guard_id") == guard)!;
                if (remove) inventory.Remove(item);
                else item["witness_ids"] = new JsonArray();
                Require(!InventoryValid(changed), "Missing registration/witness is rejected");
                meta++;
            }
        }
        Loaders(registry, catalog, directory, byId["lp03-multi-tfm-versions"]);
        Boundaries(registry, directory, byId["lp03-multi-tfm-versions"], templates);
        Retained(registry, directory, retainedSuite);
        Console.WriteLine($"LP-03: 43 exact replays, SHA-256 {Catalog.Digest(output)}; " +
            $"66 guard families, {executed.Count} witnesses ({recipes.Count} native fixed recipes), {meta} omission meta-checks.");
    }

    private sealed class Packet
    {
        internal List<PinnedInput> Inputs { get; } = [];
        internal Dictionary<string, CheckedInput> Checked { get; } = new(StringComparer.Ordinal);
        internal ResolverCase Case => new("opaque-runtime-request", "LP-03", Inputs, Checked);
        internal static Packet Load(ReplayCase source, string directory)
        {
            var result = new Packet();
            foreach (var pin in source.Inputs)
            {
                var bytes = File.ReadAllBytes(Path.Combine(directory, pin.Path.Replace('/', Path.DirectorySeparatorChar)));
                Require(Catalog.Digest(bytes) == pin.Sha256, "Witness source checked bytes");
                result.Inputs.Add(pin);
                result.Checked.Add(pin.Role, new(pin, bytes));
            }
            return result;
        }
        internal JsonNode Document(string role) => JsonNode.Parse(Checked[role].Content.Span)!;
        internal void Replace(string role, byte[] bytes)
        {
            var previous = Checked[role].Identity;
            var pin = previous with { Sha256 = Catalog.Digest(bytes) };
            Inputs[Inputs.IndexOf(previous)] = pin;
            Checked[role] = new(pin, bytes);
        }
        internal void Replace(string role, JsonNode node) => Replace(role, JsonSerializer.SerializeToUtf8Bytes(node));
        internal void Drop(string role)
        {
            Inputs.Remove(Checked[role].Identity);
            Checked.Remove(role);
        }
        internal void Rename(string previous, string role)
        {
            var value = Checked[previous];
            var pin = value.Identity with { Role = role };
            Inputs[Inputs.IndexOf(value.Identity)] = pin;
            Checked.Remove(previous);
            Checked.Add(role, new(pin, value.Content));
        }
        internal void Relocate(string role)
        {
            var value = Checked[role];
            var pin = value.Identity with { Path = $"unopened-lp03/{role}.bin" };
            Inputs[Inputs.IndexOf(value.Identity)] = pin;
            Checked[role] = new(pin, value.Content);
        }
    }

    private static void Mutate(Packet packet, string recipe)
    {
        var scope = packet.Document("assessment");
        var graph = packet.Document("dependency-graph");
        var capture = packet.Document("acquisition-receipt");
        var reviews = packet.Document("license-reviews");
        var packages = capture["packages"]!.AsArray();
        JsonNode Subject() => packages[recipe.StartsWith("owner-", StringComparison.Ordinal) ? 0 : packages.Count - 1]!;
        JsonNode Review() => reviews["reviews"]!.AsArray().Single(r => Text(r!, "key") == Text(Subject(), "key"))!;
        JsonNode Grant() => Subject()["grant"]!;
        var zeros = new string('0', 64);
        void Repack(string? id = null, string? version = null, string? grantEntry = null)
        {
            var item = Subject();
            var role = Text(item, "role");
            using var source = new ZipArchive(new MemoryStream(packet.Checked[role].Content.ToArray()));
            using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                foreach (var entry in source.Entries)
                {
                    var name = grantEntry is not null && entry.FullName == "LICENSE.txt" ? grantEntry : entry.FullName;
                    var added = zip.CreateEntry(name, CompressionLevel.NoCompression);
                    added.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    using var read = entry.Open();
                    using var write = added.Open();
                    if (entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal))
                    {
                        var xml = XDocument.Load(read);
                        var metadata = xml.Root!.Elements().Single(e => e.Name.LocalName == "metadata");
                        if (id is not null) metadata.Elements().Single(e => e.Name.LocalName == "id").Value = id;
                        if (version is not null) metadata.Elements().Single(e => e.Name.LocalName == "version").Value = version;
                        metadata.Elements().Single(e => e.Name.LocalName == "license").Value = Text(item, "declared_expression");
                        xml.Save(write);
                    }
                    else read.CopyTo(write);
                }
            }
            var bytes = output.ToArray();
            packet.Replace(role, bytes);
            item["identity"]!["sha256"] = Catalog.Digest(bytes);
            Review()["package_sha256"] = Catalog.Digest(bytes);
        }
        switch (recipe)
        {
            case "checked-grant-path": Grant()["path"] = "grants/different-retained-path.txt"; break;
            case "checked-grant-digest":
                var grantRole = Text(Grant(), "role");
                packet.Replace(grantRole, [.. packet.Checked[grantRole].Content.ToArray(), .. "\nRetained-only fixed witness.\n"u8]);
                break;
            case "review-grant-digest": Review()["grant_sha256"] = zeros; break;
            case "packaged-entry-path": Grant()["package_entry"] = "licenses/Absent.txt"; break;
            case "receipt-graph-digest": break;
            case "graph-target-set": graph["targets"]![1]!["tfm"] = "net10.0"; break;
            case "unreachable-node":
                foreach (var target in graph["targets"]!.AsArray())
                    foreach (var node in target!["nodes"]!.AsArray())
                    {
                        var edges = node!["dependencies"]!.AsArray();
                        foreach (var edge in edges.Where(e => e!.GetValue<string>() == Text(Subject(), "key")).ToArray())
                            edges.Remove(edge);
                    }
                break;
            case "missing-policy": packet.Drop("policy"); break;
            case "missing-corpus": packet.Drop("corpus"); break;
            case "dependency-digest":
                Subject()["identity"]!["sha256"] = zeros;
                Review()["package_sha256"] = zeros;
                break;
            case "dependency-id": Repack(id: "sample.different"); break;
            case "dependency-version": Repack(version: "9.0.0"); break;
            case "grant-role-index":
                packet.Rename(Text(Grant(), "role"), "grant-009");
                Grant()["role"] = "grant-009";
                break;
            case "inventory-missing":
                var subject = Subject();
                reviews["reviews"]!.AsArray().Remove(Review());
                packet.Drop(Text(subject, "role"));
                packet.Drop(Text(Grant(), "role"));
                packages.Remove(subject);
                break;
            case "inventory-extra":
                var extra = Subject().DeepClone();
                extra["key"] = "sample.unlisted/1.0.0";
                extra["role"] = "dependency-003";
                extra["identity"]!["id"] = "sample.unlisted";
                extra["grant"]!["role"] = "grant-003";
                packages.Add(extra);
                break;
            case "inventory-duplicate": packages.Add(Subject().DeepClone()); break;
            case "owner-license":
                Subject()["declared_expression"] = "MIT";
                Review()["license_id"] = "MIT";
                Repack();
                break;
            case "owner-basis": Review()["basis"] = "published-package-grant"; break;
            case "owner-disposition": Review()["disposition"] = "unresolved"; break;
            case "alternate-packaged-entry":
                Grant()["package_entry"] = "licenses/COPYING.txt";
                Repack(grantEntry: "licenses/COPYING.txt");
                break;
            case "root-digest":
                foreach (var document in new[] { scope, graph, capture }) document["root"]!["sha256"] = zeros;
                break;
            case "root-version":
                foreach (var document in new[] { scope, graph, capture }) document["root"]!["version"] = "9.0.0";
                break;
            case "changed-corpus-pin":
                packet.Replace("corpus", [.. packet.Checked["corpus"].Content.ToArray(), (byte)'\n']);
                break;
            case "dependency-key":
                var previous = Text(Subject(), "key");
                Review()["key"] = "sample.z/1.0.0";
                Subject()["key"] = "sample.z/1.0.0";
                foreach (var target in graph["targets"]!.AsArray())
                {
                    ReplaceEdges(target!["direct"]!.AsArray(), previous);
                    foreach (var node in target["nodes"]!.AsArray())
                    {
                        if (Text(node!, "key") == previous) node!["key"] = "sample.z/1.0.0";
                        ReplaceEdges(node!["dependencies"]!.AsArray(), previous);
                    }
                }
                break;
            case "review-license": Review()["license_id"] = "MIT"; break;
            case "graph-diagnostics": graph["unresolved"] = new JsonArray("synthetic unresolved acquisition observation"); break;
            case "duplicate-node":
                var nodes = graph["targets"]![0]!["nodes"]!.AsArray();
                nodes.Add(nodes.Last()!.DeepClone());
                break;
            case "duplicate-direct-edge":
                var direct = graph["targets"]![0]!["direct"]!.AsArray();
                direct.Add(direct[0]!.DeepClone());
                break;
            case "duplicate-adjacency-edge":
                graph["targets"]![0]!["nodes"]![0]!["dependencies"]!.AsArray().Add(Text(Subject(), "key"));
                break;
            case "missing-assessment": packet.Drop("assessment"); break;
            case "review-scope": Review()["scope"] = "declaration-only"; break;
            case "empty-target-scope":
                scope["target_frameworks"] = new JsonArray();
                graph["targets"] = new JsonArray();
                capture["target_frameworks"] = new JsonArray();
                break;
            case "capture-producer": capture["producer"] = "unaccepted-producer/1.0.0"; break;
            default: throw new InvalidDataException($"Unregistered fixed LP-03 recipe: {recipe}");
        }
        var counts = new JsonArray();
        foreach (var target in graph["targets"]!.AsArray())
        {
            var nodes = target!["nodes"]!.AsArray().GroupBy(n => Text(n!, "key")).Select(g => g.Last()!).ToArray();
            counts.Add(new JsonObject
            {
                ["tfm"] = Text(target, "tfm"), ["node_count"] = nodes.Length,
                ["edge_count"] = target["direct"]!.AsArray().Count + nodes.Sum(n => n["dependencies"]!.AsArray().Count)
            });
        }
        capture["completeness_basis"]!["targets"] = counts;
        if (recipe != "missing-assessment") packet.Replace("assessment", scope);
        packet.Replace("dependency-graph", graph);
        capture["graph_sha256"] = recipe == "receipt-graph-digest" ? zeros : packet.Checked["dependency-graph"].Identity.Sha256;
        packet.Replace("acquisition-receipt", capture);
        packet.Replace("license-reviews", reviews);
    }

    private static void ReplaceEdges(JsonArray edges, string previous)
    {
        for (var index = 0; index < edges.Count; index++)
            if (edges[index]!.GetValue<string>() == previous) edges[index] = "sample.z/1.0.0";
    }

    private static void Match(Registry registry, Packet packet, CaseResult template, string name)
    {
        var expected = template with
        {
            Inputs = packet.Inputs,
            EvidenceReferences = packet.Inputs.Select(p => $"input:{p.Role}:{p.Sha256}").ToArray()
        };
        var actual = registry.Dispatch(packet.Case);
        Require(Canonical.Write(actual.Write).SequenceEqual(Canonical.Write(expected.Write)),
            $"{name}: expected {expected.BranchId}, actual {actual.BranchId}");
    }

    private static void Loaders(Registry registry, Catalog catalog, string directory, ReplayCase positive)
    {
        var handler = new RejectDispatch();
        var guarded = new Registry(catalog, registry.Rows.Select(row => row.Id == "LP-03"
            ? new RegistryEntry(row.Id, "implemented", handler.ResolverId, handler.Version)
            : new RegistryEntry(row.Id, "tbd")), [handler]);
        var controls = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, "rejected-inputs.json")))!["controls"]!.AsArray();
        Require(controls.Count == 5, "Five accepted loader controls");
        var path = Path.Combine(directory, $"loader-lp03-{Guid.NewGuid():N}.json");
        try
        {
            foreach (var control in controls)
            {
                var test = JsonSerializer.SerializeToNode(positive, Options)!;
                test["inputs"] = control!["inputs"]!.DeepClone();
                File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new JsonObject
                {
                    ["schema_version"] = 1, ["cases"] = new JsonArray(test)
                }));
                Reject(() => Replay.Run(path, guarded));
                Require(handler.Calls == 0, "Loader rejects before dispatch");
            }
        }
        finally { File.Delete(path); }
    }

    private sealed class RejectDispatch : IDeterministicResolver
    {
        public int Calls { get; private set; }
        public string RowId => "LP-03";
        public string ResolverId => "lp03-dependency-licenses";
        public string Version => "1.0.0";
        public CaseResult Resolve(ResolverCase input)
        {
            Calls++;
            throw new InvalidOperationException("Rejected input reached handler.");
        }
    }

    private static void Boundaries(Registry registry, string directory, ReplayCase positive,
        Dictionary<string, CaseResult> templates)
    {
        var checks = 0;
        void Check(string name, Action<Packet> mutate, string branch = "input-invalid")
        {
            var packet = Packet.Load(positive, directory);
            mutate(packet);
            Match(registry, packet, templates[branch], name);
            checks++;
        }
        var initial = Packet.Load(positive, directory);
        var assessment = initial.Checked["assessment"].Content.ToArray();
        Check("inclusive JSON byte cap", p => p.Replace("assessment",
            [.. assessment, .. Enumerable.Repeat((byte)' ', 65536 - assessment.Length)]), "dependencies-policy-satisfied");
        Check("JSON above cap", p => p.Replace("assessment", new byte[65537]));
        Check("JSON BOM", p => p.Replace("assessment", [239, 187, 191, .. assessment]));
        Check("invalid UTF-8", p => p.Replace("assessment", [255]));
        Check("unpaired escaped surrogate", p => p.Replace("assessment", "{\"schema_version\":1,\"root\":\"\\ud800\"}"u8.ToArray()));
        Check("bool schema", p => Change(p, "assessment", n => n["schema_version"] = true));
        Check("floating schema", p => p.Replace("assessment", Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(assessment).Replace("\"schema_version\": 1", "\"schema_version\": 1.0", StringComparison.Ordinal))));
        Check("missing required field", p => Change(p, "assessment", n => n.AsObject().Remove("schema_version")));
        Check("null root", p => Change(p, "assessment", n => n["root"] = null));
        Check("null target element", p => Change(p, "assessment", n => n["target_frameworks"]![0] = null));
        Check("duplicate target", p => Change(p, "assessment", n => n["target_frameworks"]![1] = n["target_frameworks"]![0]!.DeepClone()));
        Check("excessive targets", p => Change(p, "assessment", n => n["target_frameworks"] = new JsonArray("a", "b", "c", "d", "e")));
        Check("escaped non-ASCII target", p => Change(p, "assessment", n => n["target_frameworks"]![0] = "\u0131"));
        Check("nonfinite scalar", p => p.Replace("assessment", "{\"schema_version\":NaN}"u8.ToArray()));
        Check("document depth", p => p.Replace("assessment", "{\"a\":[[[[[[[[null]]]]]]]]}"u8.ToArray()));
        Check("root archive cap", p => p.Replace("package", new byte[8 * 1024 * 1024 + 1]));
        Check("dependency archive cap", p => p.Replace("dependency-000", new byte[8 * 1024 * 1024 + 1]));
        Check("empty grant", p => p.Replace("grant-002", []));
        Check("grant BOM", p => p.Replace("grant-002", [239, 187, 191, (byte)'x']));
        Check("grant Python whitespace", p => p.Replace("grant-002", [0x1c, 0x20]));
        Check("grant invalid UTF-8", p => p.Replace("grant-002", [255]));
        Check("unknown checked role", p =>
        {
            var pin = new PinnedInput("unsupported", "safe.txt", Catalog.Digest("x"u8));
            p.Inputs.Add(pin);
            p.Checked.Add(pin.Role, new(pin, "x"u8.ToArray()));
        });
        Check("invalid direct input path", p =>
        {
            var old = p.Checked["policy"];
            var pin = old.Identity with { Path = "../escape.json" };
            p.Inputs[p.Inputs.IndexOf(old.Identity)] = pin;
            p.Checked["policy"] = new(pin, old.Content);
        });
        foreach (var variant in new[] { "truncated", "crc", "dtd", "foreign-license", "alias", "symlink" })
        {
            Check($"archive safety {variant}", p =>
            {
                var changed = ChangeArchive(p.Checked["package"].Content.ToArray(), variant);
                p.Replace("package", changed);
                foreach (var role in new[] { "assessment", "dependency-graph", "acquisition-receipt" })
                    Change(p, role, n => n["root"]!["sha256"] = Catalog.Digest(changed));
                Change(p, "acquisition-receipt", n => n["graph_sha256"] = p.Checked["dependency-graph"].Identity.Sha256);
            });
        }
        Check("aggregate checked-byte cap", p =>
        {
            foreach (var role in new[] { "policy", "corpus" }) p.Replace(role, new byte[33 * 1024 * 1024]);
        });
        Check("only checked buffers/no input reopening", p =>
        {
            foreach (var role in p.Checked.Keys.ToArray()) p.Relocate(role);
            Change(p, "acquisition-receipt", n =>
            {
                foreach (var item in n["packages"]!.AsArray())
                    item!["grant"]!["path"] = p.Checked[Text(item["grant"]!, "role")].Identity.Path;
            });
            Require(p.Inputs.All(pin => !File.Exists(pin.Path)), "Every relocated input path is absent");
        }, "dependencies-policy-satisfied");
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var name in new[] { "tr-TR", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
                Match(registry, initial, templates["dependencies-policy-satisfied"], "Culture-independent bindings");
                checks++;
            }
        }
        finally { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = uiCulture; }
        var invalid = Packet.Load(positive, directory);
        var old = invalid.Checked["package"];
        invalid.Checked["package"] = old with { Content = "tampered"u8.ToArray() };
        Reject(() => registry.Dispatch(invalid.Case));
        invalid.Checked["package"] = old with { Identity = old.Identity with { Path = "different.nupkg" } };
        Reject(() => registry.Dispatch(invalid.Case));
        checks += 2;
        Console.WriteLine($"LP-03 runtime: {checks} bounded parser/resource/checked-buffer/culture checks passed.");
    }

    private static byte[] ChangeArchive(byte[] bytes, string variant)
    {
        if (variant == "truncated") return bytes[..^1];
        if (variant == "crc")
        {
            var changed = bytes.ToArray();
            changed[45] ^= 1;
            return changed;
        }
        using var source = new ZipArchive(new MemoryStream(bytes));
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var entry in source.Entries)
            {
                var added = zip.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
                if (variant == "symlink") added.ExternalAttributes = 0xa000 << 16;
                using var read = entry.Open();
                using var write = added.Open();
                if (entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal) && variant is "dtd" or "foreign-license")
                {
                    var xml = XDocument.Load(read);
                    if (variant == "dtd") xml.AddFirst(new XDocumentType("package", null, null, "<!ENTITY x 'unsafe'>"));
                    else xml.Root!.Elements().Single(e => e.Name.LocalName == "metadata")
                        .Add(new XElement(XName.Get("license", "urn:foreign"), new XAttribute("type", "expression"), "MIT"));
                    xml.Save(write);
                }
                else read.CopyTo(write);
            }
            if (variant == "alias")
            {
                using var added = zip.CreateEntry("LICENSE.TXT").Open();
                added.WriteByte((byte)'x');
            }
        }
        return output.ToArray();
    }

    private static void Change(Packet packet, string role, Action<JsonNode> change)
    {
        var document = packet.Document(role);
        change(document);
        packet.Replace(role, document);
    }

    private static void Retained(Registry registry, string directory, CaseSuite suite)
    {
        var vendors = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, "retained-inputs.json")))!["vendors"]!.AsArray();
        Dictionary<string, byte[]> ReadBindings(Dictionary<string, string> bindings)
        {
            Require(bindings.Count == 2 && vendors.All(v => bindings.ContainsKey(Text(v!, "binding"))), "Exact retained binding names");
            var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var vendor in vendors)
            {
                var name = Text(vendor!, "binding");
                var path = bindings[name];
                Require(new FileInfo(path).Length == vendor!["size_bytes"]!.GetValue<long>(), "Retained archive length");
                var bytes = File.ReadAllBytes(path);
                Require(bytes.LongLength == vendor["size_bytes"]!.GetValue<long>() &&
                    Catalog.Digest(bytes) == Text(vendor, "sha256"), "Retained checked archive digest");
                using var zip = new ZipArchive(new MemoryStream(bytes));
                var nuspec = zip.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
                Require(nuspec.Length <= 1024 * 1024, "Retained identity read bounded");
                using var read = nuspec.Open();
                using var reader = XmlReader.Create(read, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024
                });
                var xml = XDocument.Load(reader);
                var metadata = xml.Root!.Elements().Single(e => e.Name.LocalName == "metadata");
                Require(metadata.Element(metadata.Name.Namespace + "id")!.Value.ToLowerInvariant() == Text(vendor, "package_id") &&
                    metadata.Element(metadata.Name.Namespace + "version")!.Value == Text(vendor, "version"), "Actual retained archive identity");
                result.Add(Text(vendor, "vendor"), bytes);
            }
            return result;
        }
        var temp = Path.Combine(Path.GetTempPath(), $"lp03-bindings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        var bad = Path.Combine(temp, "invalid.nupkg");
        try
        {
            Reject(() => ReadBindings(new()));
            Reject(() => ReadBindings(new() { ["LP03_SYNCFUSION_NUPKG"] = bad }));
            Reject(() => ReadBindings(new() { ["LP03_SYNCFUSION_NUPKG"] = bad, ["LP03_MISSPELLED_NUPKG"] = bad }));
            var both = new Dictionary<string, string> { ["LP03_SYNCFUSION_NUPKG"] = bad, ["LP03_INFRAGISTICS_NUPKG"] = bad };
            Reject(() => ReadBindings(both));
            File.WriteAllBytes(bad, [0]);
            Reject(() => ReadBindings(both));
            File.WriteAllBytes(bad, new byte[4912251]);
            Reject(() => ReadBindings(both));
        }
        finally { File.Delete(bad); Directory.Delete(temp); }
        var bindings = Environment.GetEnvironmentVariables().Keys.OfType<string>()
            .Where(name => name.StartsWith("LP03_", StringComparison.Ordinal))
            .ToDictionary(name => name, name => Environment.GetEnvironmentVariable(name)!, StringComparer.Ordinal);
        if (bindings.Count == 0)
        {
            Console.WriteLine("LP-03 retained: six binding negatives passed; actual archives not bound.");
            return;
        }
        var buffers = ReadBindings(bindings);
        var results = new List<(string Id, CaseResult Result)>();
        foreach (var source in suite.Cases)
        {
            Require(source.Inputs.Count == 1, "Retained packet has only a root, not inferred scope");
            var pin = source.Inputs.Single();
            var bytes = buffers.Values.Single(value => Catalog.Digest(value) == pin.Sha256);
            var packet = new Packet();
            packet.Inputs.Add(pin);
            packet.Checked.Add(pin.Role, new(pin, bytes));
            Match(registry, packet, source.Expected, source.CaseId);
            var result = registry.Dispatch(packet.Case);
            Require(result.BranchId == "scope-missing" && result.Decision == "escalated" &&
                result.Applicability == "unknown" && result.Finding is null, "No retained positive or gap");
            results.Add((source.CaseId, result));
        }
        var output = Canonical.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WritePropertyName("cases");
            writer.WriteStartArray();
            foreach (var (id, result) in results)
            {
                writer.WriteStartObject();
                writer.WriteString("case_id", id);
                writer.WritePropertyName("result");
                result.Write(writer);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
        Console.WriteLine($"LP-03 retained: two exact identities/digests, scope-missing/not tested; " +
            $"six binding negatives passed; SHA-256 {Catalog.Digest(output)}.");
    }

    private static string Text(JsonNode node, string key) => node[key]!.GetValue<string>();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException($"LP-03 check failed: {message}");
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or JsonException or FileNotFoundException) { return; }
        throw new InvalidOperationException("Expected LP-03 input rejection.");
    }
}
