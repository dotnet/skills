using System.Text;
using System.Text.Json;

namespace BlazorPackageResolver;

internal static class Lp01CaseFold
{
    private const string ResourceName = "BlazorPackageResolver.Lp01CaseFold";
    private const string Digest = "1c1dd710665f23ad059dc9c5c986d48f9cad77c3f58de6cf0cad4590e066e7e7";
    private static readonly Lazy<IReadOnlyDictionary<int, string>> Mapping = new(ReadMapping);

    internal static string Fold(string name)
    {
        var result = new StringBuilder(name.Length);
        foreach (var rune in name.EnumerateRunes())
            result.Append(Mapping.Value.TryGetValue(rune.Value, out var folded) ? folded : rune.ToString());
        return result.ToString();
    }

    private static IReadOnlyDictionary<int, string> ReadMapping()
    {
        using var resource = typeof(Lp01CaseFold).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("Pinned LP-01 Unicode 15.0.0 casefold map is missing.");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        var bytes = buffer.ToArray();
        if (bytes.Length > 32768 || Catalog.Digest(bytes) != Digest)
            throw new InvalidDataException("Pinned LP-01 Unicode 15.0.0 casefold map is inconsistent.");
        using var doc = JsonDocument.Parse(bytes);
        var mappings = new Dictionary<int, string>();
        foreach (var item in doc.RootElement.EnumerateObject())
        {
            if (!int.TryParse(item.Name, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var codepoint) ||
                !Rune.IsValid(codepoint) || item.Value.ValueKind != JsonValueKind.String ||
                !mappings.TryAdd(codepoint, item.Value.GetString()!))
                throw new InvalidDataException("Pinned LP-01 casefold mapping is invalid.");
        }
        return mappings;
    }
}
