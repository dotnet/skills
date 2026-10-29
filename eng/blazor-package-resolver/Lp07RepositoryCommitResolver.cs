using System.Xml.Linq;

namespace BlazorPackageResolver;

public sealed class Lp07RepositoryCommitResolver : IDeterministicResolver
{
    private const string MissingRepository =
        "RepositoryCommit does not identify a commit SHA; the nuspec metadata has no direct repository element.";

    public string RowId => "LP-07";
    public string ResolverId => "lp07-repository-commit";
    public string Version => "1.0.0";

    public CaseResult Resolve(ResolverCase input)
    {
        if (input.Inputs.Count == 0)
            return Escalate(input, "missing-package", "unknown",
                "The package bytes required for LP-07 were not supplied.", "package", "input-owner");
        if (input.Inputs.Count != 1 || input.Inputs[0].Role != "package" ||
            !input.CheckedInputs.TryGetValue("package", out var checkedPackage))
            throw new InvalidDataException("LP-07 requires exactly one checked package input.");

        var parsed = NuGetNuspecReader.Read(checkedPackage.Content);
        return parsed.Status switch
        {
            NuspecStatus.Ready => InspectMetadata(input, parsed.Metadata!),
            NuspecStatus.AmbiguousNuspec => Escalate(input, "ambiguous-nuspec", "unknown",
                "The archive has multiple root nuspec entries.", "unique-root-nuspec", "package-owner"),
            NuspecStatus.MissingNuspec => Escalate(input, "missing-nuspec", "unknown",
                "The archive has no nuspec entry.", "root-nuspec", "package-owner"),
            NuspecStatus.MissingRootNuspec => Escalate(input, "missing-root-nuspec", "unknown",
                "The only nuspec entry is nested in a subfolder.", "root-nuspec", "package-owner"),
            NuspecStatus.OversizedNuspec => Escalate(input, "unsafe-nuspec", "unknown",
                "The nuspec exceeds the safe inspection size limit.", "safe-nuspec", "package-owner"),
            NuspecStatus.UnsafeNuspec => Escalate(input, "unsafe-nuspec", "unknown",
                "The nuspec contains a DTD/entity declaration and cannot be safely inspected.",
                "safe-nuspec", "package-owner"),
            NuspecStatus.InvalidXml => Escalate(input, "invalid-xml", "unknown",
                "The nuspec XML is not well formed.", "valid-nuspec", "package-owner"),
            NuspecStatus.InvalidMetadata => Escalate(input, "invalid-xml", "unknown",
                "The nuspec requires exactly one recognized metadata element.",
                "valid-nuspec", "package-owner"),
            NuspecStatus.AmbiguousMetadata => Escalate(input, "ambiguous-metadata", "unknown",
                NuGetNuspecReader.AmbiguousMetadataMissingFact, "unambiguous-nuspec", "package-owner"),
            NuspecStatus.ForeignRoot => Escalate(input, "foreign-root", "unknown",
                "The package root is not in a recognized NuGet namespace.",
                "recognized-nuspec", "package-owner"),
            NuspecStatus.UnreadablePackage => Escalate(input, "unreadable-package", "unknown",
                "The supplied checked bytes are not a readable NuGet archive.",
                "valid-nupkg", "package-owner"),
            _ => throw new InvalidDataException("Unknown nuspec inspection result.")
        };
    }

    private CaseResult InspectMetadata(ResolverCase input, XElement metadata)
    {
        var repositories = metadata.Elements()
            .Where(element => element.Name.LocalName == "repository").ToArray();
        if (repositories.Any(element => element.Name.Namespace != metadata.Name.Namespace))
            return Escalate(input, "foreign-repository", "applicable",
                "A foreign-namespace repository element cannot establish NuGet RepositoryCommit.",
                "recognized-repository-metadata", "package-owner");
        if (repositories.Length > 1)
            return Escalate(input, "ambiguous", "applicable",
                repositories.Select(element => NormalizeCommit(element.Attribute("commit")?.Value))
                    .Distinct(StringComparer.Ordinal).Count() > 1
                    ? "Conflicting repository elements prevent an unambiguous RepositoryCommit."
                    : "Multiple repository elements prevent an unambiguous RepositoryCommit.",
                "unambiguous-nuspec", "package-owner");
        if (repositories.Length == 0)
            return Deterministic(input, "missing-repository", MissingRepository, "package:nuspec/metadata");

        var repository = repositories[0];
        if (repository.HasElements || !string.IsNullOrWhiteSpace(repository.Value))
            return Escalate(input, "invalid-xml", "applicable",
                "The repository element contains unsupported content.", "corrected-nuspec", "package-owner");
        if (repository.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration &&
            attribute.Name != "url" && attribute.Name != "type" &&
            attribute.Name != "branch" && attribute.Name != "commit"))
            return Escalate(input, "unsupported-attribute", "applicable",
                repository.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration &&
                    !string.IsNullOrEmpty(attribute.Name.NamespaceName))
                    ? "An unknown namespaced repository attribute is outside the recognized metadata contract."
                    : "An unknown repository attribute is outside the recognized metadata contract.",
                "corrected-nuspec", "package-owner");

        var commit = NormalizeCommit(repository.Attribute("commit")?.Value);
        if (commit is null)
            return Deterministic(input, "missing-commit",
                "RepositoryCommit does not identify a commit SHA; the repository has no commit attribute.",
                "package:nuspec/repository");
        if (commit.Length == 0)
            return Deterministic(input, "blank-commit",
                "RepositoryCommit does not identify a commit SHA; the commit attribute is blank.",
                "package:nuspec/repository");
        if (commit.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            return Deterministic(input, "invalid-characters",
                "RepositoryCommit does not identify a full commit SHA; the object ID is not ASCII hexadecimal.",
                "package:nuspec/repository");
        if (commit.Length is 40 or 64)
        {
            if (commit.All(c => c == '0'))
                return Deterministic(input, "null-oid",
                    "RepositoryCommit does not identify a commit SHA; an all-zero object ID is a null sentinel.",
                    "package:nuspec/repository");
            var algorithm = commit.Length == 40 ? "1" : "256";
            return Deterministic(input, $"full-sha{algorithm}",
                $"RepositoryCommit declares a full SHA-{algorithm} commit SHA: {commit}.",
                "package:nuspec/repository");
        }
        return commit.Length is (>= 4 and <= 39) or (>= 41 and <= 63)
            ? Deterministic(input, "abbreviated-commit",
                "RepositoryCommit does not identify a full commit SHA; abbreviated object IDs are repository-dependent.",
                "package:nuspec/repository")
            : Deterministic(input, "invalid-length",
                "RepositoryCommit does not identify a full commit SHA; the object ID has the wrong length.",
                "package:nuspec/repository");
    }

    private static string? NormalizeCommit(string? value)
    {
        if (value is null)
            return null;
        var trimmed = value.Trim();
        return string.Create(trimmed.Length, trimmed, static (output, source) =>
        {
            for (var index = 0; index < source.Length; index++)
            {
                var c = source[index];
                output[index] = c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
            }
        });
    }

    private CaseResult Deterministic(ResolverCase input, string branch, string finding, string reference) =>
        new(RowId, ResolverId, Version, branch, "deterministic", "applicable",
            input.Inputs, [reference], null, finding, null, null, null);

    private CaseResult Escalate(ResolverCase input, string branch, string applicability,
        string missingFact, string requiredInput, string nextCategory) =>
        new(RowId, ResolverId, Version, branch, "escalated", applicability,
            input.Inputs, [], null, null, missingFact, requiredInput, nextCategory);
}
