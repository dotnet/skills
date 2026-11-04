using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazorComponentReadiness.Validator.IO;

public static partial class NuGetVersionNormalizer
{
    [GeneratedRegex("^[0-9A-Za-z-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex LabelPattern();

    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is 0 or > 256 ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            !value.IsNormalized(NormalizationForm.FormC) ||
            value.Any(char.IsControl))
        {
            throw new DeterministicValidationException(
                "The package version is empty, untrimmed, non-NFC, too long, or contains controls.");
        }

        var metadataIndex = value.IndexOf('+');
        if (metadataIndex != value.LastIndexOf('+'))
        {
            throw new DeterministicValidationException(
                "The package version contains multiple metadata separators.");
        }

        var metadata = metadataIndex >= 0 ? value[(metadataIndex + 1)..] : null;
        var withoutMetadata = metadataIndex >= 0 ? value[..metadataIndex] : value;
        var prereleaseIndex = withoutMetadata.IndexOf('-');
        var prerelease = prereleaseIndex >= 0 ? withoutMetadata[(prereleaseIndex + 1)..] : null;
        var core = prereleaseIndex >= 0 ? withoutMetadata[..prereleaseIndex] : withoutMetadata;

        var coreParts = core.Split('.');
        if (coreParts.Length is < 1 or > 4)
        {
            throw new DeterministicValidationException(
                "The package version core must contain one to four numeric parts.");
        }

        var normalizedCore = new List<string>(4);
        foreach (var part in coreParts)
        {
            if (part.Length == 0 ||
                part.Any(character => !char.IsAsciiDigit(character)) ||
                !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                throw new DeterministicValidationException(
                    "The package version core contains an invalid numeric part.");
            }

            normalizedCore.Add(number.ToString(CultureInfo.InvariantCulture));
        }

        while (normalizedCore.Count < 3)
        {
            normalizedCore.Add("0");
        }

        if (normalizedCore.Count == 4 && normalizedCore[3] == "0")
        {
            normalizedCore.RemoveAt(3);
        }

        var builder = new StringBuilder(string.Join('.', normalizedCore));
        if (prerelease is not null)
        {
            builder.Append('-');
            builder.Append(NormalizeLabels(prerelease, "prerelease"));
        }

        if (metadata is not null)
        {
            builder.Append('+');
            builder.Append(NormalizeLabels(metadata, "metadata"));
        }

        var normalized = builder.ToString();
        if (normalized.Length > 256)
        {
            throw new DeterministicValidationException(
                "The canonical package version exceeds 256 characters.");
        }

        return normalized;
    }

    private static string NormalizeLabels(string value, string kind)
    {
        var labels = value.Split('.');
        if (labels.Any(label => label.Length == 0 || !LabelPattern().IsMatch(label)))
        {
            throw new DeterministicValidationException(
                $"The package version {kind} contains an invalid identifier.");
        }

        return string.Join(
            '.',
            labels.Select(label =>
            {
                var normalized = label.ToLowerInvariant();
                if (normalized.All(char.IsAsciiDigit))
                {
                    normalized = normalized.TrimStart('0');
                    return normalized.Length == 0 ? "0" : normalized;
                }

                return normalized;
            }));
    }
}
