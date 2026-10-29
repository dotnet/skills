using System.Xml.Linq;

namespace BlazorPackageResolver;

public sealed class Lp09ProjectUrlResolver : IDeterministicResolver
{
    private static readonly HashSet<string> KnownNuGetNamespaces = new(StringComparer.Ordinal)
    {
        "http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2011/10/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2013/01/nuspec.xsd",
        "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"
    };

    public string RowId => "LP-09";
    public string ResolverId => "lp09-project-url";
    public string Version => "1.0.0";

    public CaseResult Resolve(ResolverCase input)
    {
        if (input.Inputs.Count == 0)
            return Escalate(input, "missing-package", "unknown",
                "The package bytes required for LP-09 were not supplied.", "package", "input-owner");
        if (input.Inputs.Count != 1 || input.Inputs[0].Role != "package" ||
            !input.CheckedInputs.TryGetValue("package", out var checkedPackage))
            throw new InvalidDataException("LP-09 requires exactly one checked package input.");

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
        var candidates = metadata.Elements()
            .Where(element => string.Equals(element.Name.LocalName, "projectUrl",
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Any(element => element.Name.LocalName != "projectUrl"))
            return Escalate(input, "case-variant-project-url", "applicable",
                candidates.Any(element => element.Name.LocalName == "projectUrl")
                    ? "An exact projectUrl beside a case-variant ProjectUrl has conflicting interpretation: the schema and ManifestReader require exact projectUrl, but case-insensitive NuGet metadata readers recognize both."
                    : "A case-variant ProjectUrl has conflicting interpretation: the schema and ManifestReader require exact projectUrl, but case-insensitive NuGet metadata readers recognize ProjectUrl.",
                "corrected-nuspec", "package-owner");

        var foreign = candidates.FirstOrDefault(element => element.Name.Namespace != metadata.Name.Namespace);
        if (foreign is not null)
        {
            var message = foreign.Name.NamespaceName.Length == 0
                ? "An unqualified projectUrl child cannot establish ProjectUrl for namespaced metadata."
                : KnownNuGetNamespaces.Contains(foreign.Name.NamespaceName)
                    ? "A projectUrl in a different recognized NuGet namespace cannot establish ProjectUrl for this metadata."
                    : "A foreign-namespace projectUrl element cannot establish NuGet ProjectUrl.";
            return Escalate(input, "foreign-project-url", "applicable", message,
                "recognized-project-url-metadata", "package-owner");
        }

        if (candidates.Length > 1)
            return Escalate(input, "ambiguous", "applicable",
                candidates.Select(element => element.Value.Trim())
                    .Distinct(StringComparer.Ordinal).Count() > 1
                    ? "Conflicting projectUrl elements prevent an unambiguous ProjectUrl value."
                    : "Multiple projectUrl elements prevent an unambiguous ProjectUrl value.",
                "unambiguous-nuspec", "package-owner");
        if (candidates.Length == 0)
            return Deterministic(input, "missing-project-url",
                "ProjectUrl is not populated; the nuspec metadata has no direct projectUrl element.",
                "package:nuspec/metadata");

        var project = candidates[0];
        if (project.HasElements || project.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration))
            return Escalate(input, "malformed-project-url", "applicable",
                "The projectUrl element has unsupported attributes or child elements.",
                "corrected-nuspec", "package-owner");

        var text = project.Value.Trim();
        return text.Length == 0
            ? Deterministic(input, "blank-project-url",
                "ProjectUrl is not populated; the projectUrl element is empty or whitespace-only.",
                "package:nuspec/projectUrl")
            : Deterministic(input, "populated", $"ProjectUrl is populated: {text}.",
                "package:nuspec/projectUrl");
    }

    private CaseResult Deterministic(ResolverCase input, string branch, string finding, string reference) =>
        new(RowId, ResolverId, Version, branch, "deterministic", "applicable",
            input.Inputs, [reference], null, finding, null, null, null);

    private CaseResult Escalate(ResolverCase input, string branch, string applicability,
        string missingFact, string requiredInput, string nextCategory) =>
        new(RowId, ResolverId, Version, branch, "escalated", applicability,
            input.Inputs, [], null, null, missingFact, requiredInput, nextCategory);
}
