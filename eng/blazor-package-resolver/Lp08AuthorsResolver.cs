using System.Xml.Linq;

namespace BlazorPackageResolver;

public sealed class Lp08AuthorsResolver : IDeterministicResolver
{
    public string RowId => "LP-08";
    public string ResolverId => "lp08-authors";
    public string Version => "1.0.0";

    public CaseResult Resolve(ResolverCase input)
    {
        if (input.Inputs.Count == 0)
            return Escalate(input, "missing-package", "unknown",
                "The package bytes required for LP-08 were not supplied.", "package", "input-owner");
        if (input.Inputs.Count != 1 || input.Inputs[0].Role != "package" ||
            !input.CheckedInputs.TryGetValue("package", out var checkedPackage))
            throw new InvalidDataException("LP-08 requires exactly one checked package input.");

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
        var authors = metadata.Elements()
            .Where(element => element.Name.LocalName == "authors").ToArray();
        if (authors.Any(element => element.Name.Namespace != metadata.Name.Namespace))
            return Escalate(input, "foreign-authors", "applicable",
                "A foreign-namespace authors element cannot establish NuGet Authors.",
                "recognized-authors-metadata", "package-owner");
        if (authors.Length > 1)
            return Escalate(input, "ambiguous", "applicable",
                authors.Select(element => element.Value.Trim())
                    .Distinct(StringComparer.Ordinal).Count() > 1
                    ? "Conflicting authors elements prevent an unambiguous Authors value."
                    : "Multiple authors elements prevent an unambiguous Authors value.",
                "unambiguous-nuspec", "package-owner");
        if (authors.Length == 0)
            return Deterministic(input, "missing-authors",
                "Authors is not populated; the nuspec has no direct authors element.",
                "package:nuspec/metadata");

        var element = authors[0];
        if (element.HasElements || element.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration))
            return Escalate(input, "malformed-authors", "applicable",
                "The authors element has unsupported attributes or child elements.",
                "corrected-nuspec", "package-owner");

        var text = element.Value.Trim();
        if (text.Length == 0)
            return Deterministic(input, "blank-authors",
                "Authors is not populated; the authors element is empty or whitespace-only.",
                "package:nuspec/authors");
        if (!text.Any(character => character != ',' && !char.IsWhiteSpace(character)))
            return Deterministic(input, "delimiter-only",
                "Authors is not populated; the authors text has no nonblank comma-separated entry.",
                "package:nuspec/authors");
        return Deterministic(input, "populated", $"Authors is populated: {text}.",
            "package:nuspec/authors");
    }

    private CaseResult Deterministic(ResolverCase input, string branch, string finding, string reference) =>
        new(RowId, ResolverId, Version, branch, "deterministic", "applicable",
            input.Inputs, [reference], null, finding, null, null, null);

    private CaseResult Escalate(ResolverCase input, string branch, string applicability,
        string missingFact, string requiredInput, string nextCategory) =>
        new(RowId, ResolverId, Version, branch, "escalated", applicability,
            input.Inputs, [], null, null, missingFact, requiredInput, nextCategory);
}
