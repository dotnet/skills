using System.Xml.Linq;

namespace BlazorPackageResolver;

public sealed class Lp05LicenseExpressionResolver : IDeterministicResolver
{
    private const string MissingExpression = "PackageLicenseExpression is not populated; the nuspec has no license expression.";
    private static readonly Version CurrentLicenseVersion = new(1, 0, 0);

    public string RowId => "LP-05";
    public string ResolverId => "lp05-license-expression";
    public string Version => "1.0.0";

    public CaseResult Resolve(ResolverCase input)
    {
        if (input.Inputs.Count == 0)
            return Escalate(input, "missing-package", "unknown",
                "The package bytes required for LP-05 were not supplied.", "package", "input-owner");
        if (input.Inputs.Count != 1 || input.Inputs[0].Role != "package" ||
            !input.CheckedInputs.TryGetValue("package", out var checkedPackage))
            throw new InvalidDataException("LP-05 requires exactly one checked package input.");

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
            NuspecStatus.OversizedNuspec => OversizedNuspec(input),
            NuspecStatus.UnsafeNuspec => UnsafeNuspec(input),
            NuspecStatus.AmbiguousMetadata => Escalate(input, "ambiguous-metadata", "unknown",
                NuGetNuspecReader.AmbiguousMetadataMissingFact, "unambiguous-nuspec", "package-owner"),
            NuspecStatus.InvalidXml or NuspecStatus.InvalidMetadata => Escalate(input, "invalid-xml", "unknown",
                "The nuspec XML is not well formed.", "valid-nuspec", "package-owner"),
            NuspecStatus.ForeignRoot => Escalate(input, "foreign-root", "unknown",
                "The package root is not in a recognized NuGet namespace.",
                "recognized-nuspec", "package-owner"),
            NuspecStatus.UnreadablePackage => Unreadable(input),
            _ => throw new InvalidDataException("Unknown nuspec inspection result.")
        };
    }

    private CaseResult InspectMetadata(ResolverCase input, XElement metadata)
    {
        var licenses = metadata.Elements()
            .Where(element => element.Name.LocalName == "license").ToArray();
        if (licenses.Any(element => element.Name.Namespace != metadata.Name.Namespace))
            return Escalate(input, "foreign-license", "applicable",
                "A foreign-namespace license element cannot establish NuGet license metadata.",
                "recognized-license-metadata", "package-owner");
        if (licenses.Length > 1)
            return Escalate(input, "ambiguous", "applicable",
                "Duplicate license elements conflict.", "unambiguous-nuspec", "package-owner");
        if (licenses.Length == 0)
            return Deterministic(input, "missing-expression", MissingExpression, "package:nuspec/metadata");

        var license = licenses[0];
        var type = license.Attribute("type");
        if (type is null)
            return Escalate(input, "malformed", "applicable",
                "The license element has no type attribute.", "corrected-nuspec", "package-owner");
        if (license.HasElements || license.Attributes().Any(attribute =>
                !attribute.IsNamespaceDeclaration && attribute.Name != "type" && attribute.Name != "version"))
            return Malformed(input);
        var versionAttribute = license.Attribute("version");
        if (versionAttribute is not null)
        {
            if (!System.Version.TryParse(versionAttribute.Value.Trim(), out var parsedVersion))
                return Malformed(input);
            if (parsedVersion.CompareTo(CurrentLicenseVersion) > 0)
                return Escalate(input, "unsupported-version", "applicable",
                    "The license expression version is newer than supported 1.0.0.",
                    "supported-license-version", "package-owner");
        }
        var value = license.Value.Trim();
        if (string.Equals(type.Value.Trim(), "file", StringComparison.OrdinalIgnoreCase))
            return value.Length == 0
                ? Deterministic(input, "missing-expression", MissingExpression, "package:nuspec/metadata")
                : Deterministic(input, "file",
                    "PackageLicenseExpression is not populated; the nuspec declares a license file.",
                    "package:nuspec/license");
        if (!string.Equals(type.Value.Trim(), "expression", StringComparison.OrdinalIgnoreCase))
            return Escalate(input, "malformed", "applicable",
                "The license type is not a supported NuGet expression or file type.",
                "corrected-nuspec", "package-owner");
        if (value.Length == 0 || value.Length > 1024)
            return Malformed(input);
        return Deterministic(input, "expression",
            $"PackageLicenseExpression is populated: {value}.", "package:nuspec/license");
    }

    private CaseResult Deterministic(ResolverCase input, string branch, string finding, string reference) =>
        new(RowId, ResolverId, Version, branch, "deterministic", "applicable",
            input.Inputs, [reference], null, finding, null, null, null);

    private CaseResult Escalate(ResolverCase input, string branch, string applicability,
        string missingFact, string requiredInput, string nextCategory) =>
        new(RowId, ResolverId, Version, branch, "escalated", applicability,
            input.Inputs, [], null, null, missingFact, requiredInput, nextCategory);

    private CaseResult Malformed(ResolverCase input) =>
        Escalate(input, "malformed", "applicable",
            "A nonblank license expression cannot be established from malformed metadata.",
            "corrected-nuspec", "package-owner");

    private CaseResult UnsafeNuspec(ResolverCase input) =>
        Escalate(input, "unsafe-nuspec", "unknown",
            "The nuspec contains a DTD/entity declaration and cannot be safely inspected.",
            "safe-nuspec", "package-owner");

    private CaseResult OversizedNuspec(ResolverCase input) =>
        Escalate(input, "unsafe-nuspec", "unknown",
            "The nuspec exceeds the safe inspection size limit.",
            "safe-nuspec", "package-owner");

    private CaseResult Unreadable(ResolverCase input) =>
        Escalate(input, "unreadable-package", "unknown",
            "The supplied checked bytes are not a readable NuGet archive.",
            "valid-nupkg", "package-owner");
}
