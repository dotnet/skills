namespace BlazorPackageResolver;

public sealed record RegistryEntry(string RowId, string WorkState, string? ResolverId = null, string? Version = null);

public interface IDeterministicResolver
{
    string RowId { get; }
    string ResolverId { get; }
    string Version { get; }
    CaseResult Resolve(ResolverCase input);
}

public sealed class Registry
{
    private const string PackageIds =
        "LP-01 LP-02 LP-03 LP-04 LP-05 LP-06 LP-07 LP-08 LP-09 LP-10 " +
        "PI-01 PI-02 PI-03 PI-04 PI-05 PI-06 PI-07 PI-08 PI-09 PI-10 PI-11 PI-12 " +
        "SEC-01 SEC-02 SEC-03 SEC-04 SEC-05 SEC-06 SEC-07 SEC-08 SEC-09 " +
        "BEQ-21 BEQ-24 TA-07 CI-01 CI-05 CI-06 CI-07 CI-08 " +
        "SUP-01 SUP-02 SUP-03 SUP-04 SUP-05 SUP-06 SUP-07 SUP-08 SUP-10 " +
        "SCF-01 SCF-02 SCF-03 SCF-04 SCF-05 SCF-06 AI-01 AI-02 AI-03 AI-04 AI-05 AI-06";

    private readonly Dictionary<string, RegistryEntry> entries;
    private readonly Dictionary<string, IDeterministicResolver> handlers;
    public IReadOnlyList<PackageRow> Rows { get; }

    public static Registry Bootstrap(Catalog catalog) =>
        new(catalog, PackageIds.Split(' ').Select(id => id switch
        {
            "LP-05" => new RegistryEntry(id, "implemented", "lp05-license-expression", "1.0.0"),
            "LP-06" => new RegistryEntry(id, "implemented", "lp06-repository-url", "1.0.0"),
            "LP-07" => new RegistryEntry(id, "implemented", "lp07-repository-commit", "1.0.0"),
            "LP-08" => new RegistryEntry(id, "implemented", "lp08-authors", "1.0.0"),
            "LP-09" => new RegistryEntry(id, "implemented", "lp09-project-url", "1.0.0"),
            "LP-10" => new RegistryEntry(id, "implemented", "lp10-binary-attribution", "1.0.0"),
            _ => new RegistryEntry(id, "tbd")
        }), [new Lp05LicenseExpressionResolver(), new Lp06RepositoryUrlResolver(),
            new Lp07RepositoryCommitResolver(), new Lp08AuthorsResolver(), new Lp09ProjectUrlResolver(),
            new Lp10BinaryAttributionResolver()]);

    public Registry(Catalog catalog, IEnumerable<RegistryEntry> registrations,
        IEnumerable<IDeterministicResolver> implementations)
    {
        Rows = catalog.PackageRows;
        var list = registrations.ToList();
        entries = new Dictionary<string, RegistryEntry>(StringComparer.Ordinal);
        foreach (var item in list)
        {
            if (catalog.ExcludedIds.Contains(item.RowId) ||
                !catalog.PackageRows.Any(row => row.Id == item.RowId) ||
                !entries.TryAdd(item.RowId, item))
                throw new InvalidDataException($"Excluded, unknown or duplicate registry ID: {item.RowId}");
        }
        if (list.Count != 60 || !catalog.PackageRows.All(row => entries.ContainsKey(row.Id)))
            throw new InvalidDataException("Registry must cover all 60 package rows exactly once.");
        handlers = new Dictionary<string, IDeterministicResolver>(StringComparer.Ordinal);
        foreach (var handler in implementations)
            if (!handlers.TryAdd(handler.RowId, handler))
                throw new InvalidDataException($"Duplicate handler: {handler.RowId}");
        foreach (var (id, entry) in entries)
        {
            if (entry.WorkState == "tbd")
            {
                if (entry.ResolverId is not null || entry.Version is not null || handlers.ContainsKey(id))
                    throw new InvalidDataException($"TBD row has resolver or handler: {id}");
            }
            else if (entry.WorkState != "implemented" || !handlers.TryGetValue(id, out var handler) ||
                string.IsNullOrWhiteSpace(entry.ResolverId) || string.IsNullOrWhiteSpace(entry.Version) ||
                handler.ResolverId != entry.ResolverId || handler.Version != entry.Version)
                throw new InvalidDataException($"Implemented row lacks matching handler: {id}");
        }
        if (handlers.Keys.Any(id => !entries.TryGetValue(id, out var entry) || entry.WorkState != "implemented"))
            throw new InvalidDataException("Orphan resolver implementation.");
    }

    public byte[] Coverage(Catalog catalog) => Canonical.Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteNumber("schema_version", 1);
        writer.WriteString("rubric_version", catalog.RubricVersion);
        writer.WriteString("rubric_sha256", catalog.RubricDigest);
        writer.WriteString("basis_sha256", catalog.BasisDigest);
        writer.WritePropertyName("rows");
        writer.WriteStartArray();
        foreach (var row in Rows)
        {
            var registration = entries[row.Id];
            writer.WriteStartObject();
            writer.WriteString("id", row.Id);
            writer.WriteString("scope", row.Scope);
            writer.WriteString("requirement", row.Requirement);
            writer.WriteString("area", row.Area);
            writer.WriteString("clause", row.Clause);
            writer.WriteString("work_state", registration.WorkState);
            if (registration.ResolverId is not null)
            {
                writer.WriteString("resolver_id", registration.ResolverId);
                writer.WriteString("resolver_version", registration.Version);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    });

    public CaseResult Dispatch(ResolverCase input)
    {
        if (string.IsNullOrWhiteSpace(input.RowId) ||
            !entries.TryGetValue(input.RowId, out var registration))
            throw new InvalidDataException($"Case row is not a package registry ID: {input.RowId}");
        var roles = new HashSet<string>(StringComparer.Ordinal);
        if (input.Inputs is null || input.CheckedInputs is null ||
            input.CheckedInputs.Count != input.Inputs.Count ||
            input.Inputs.Any(identity => identity is null ||
                string.IsNullOrWhiteSpace(identity.Role) || !roles.Add(identity.Role) ||
                !input.CheckedInputs.TryGetValue(identity.Role, out var checkedInput) ||
                checkedInput.Identity != identity ||
                Catalog.Digest(checkedInput.Content.Span) != identity.Sha256))
            throw new InvalidDataException("Pinned input identities do not match checked content.");
        if (registration.WorkState == "tbd")
            return new CaseResult(input.RowId, "package-dispatcher", "1.0.0", "unimplemented",
                "escalated", "unknown", input.Inputs, [], null, null,
                "A deterministic branch for this row has not been implemented.", "row-resolver",
                "row-implementation");
        var handler = handlers[input.RowId];
        var result = handler.Resolve(input);
        result.Validate(input, handler);
        return result;
    }
}
