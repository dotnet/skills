using System.Text;
using System.Text.Json;
using BlazorComponentReadiness.Validator.Contracts;

namespace BlazorComponentReadiness.Validator.IO;

public static class ContractJson
{
    public static void RequireProperties(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new DeterministicValidationException("JSON contract value must be an object.");
        }

        var actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (!actual.SequenceEqual(names, StringComparer.Ordinal))
        {
            throw new DeterministicValidationException(
                $"JSON properties must be exactly [{string.Join(", ", names)}] in canonical order.");
        }
    }

    public static void RequirePropertiesUnordered(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new DeterministicValidationException("JSON contract value must be an object.");
        }

        var actual = element.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != names.Length ||
            actual.Except(names, StringComparer.Ordinal).Any() ||
            names.Except(actual, StringComparer.Ordinal).Any())
        {
            throw new DeterministicValidationException(
                $"JSON properties must be exactly [{string.Join(", ", names)}].");
        }
    }

    public static string String(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new DeterministicValidationException($"JSON property '{name}' must be a string.");
        }

        return value.GetString()!;
    }

    public static string? NullableString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            throw new DeterministicValidationException($"JSON property '{name}' is required.");
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            _ => throw new DeterministicValidationException(
                $"JSON property '{name}' must be a string or null.")
        };
    }

    public static int Int32(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result))
        {
            throw new DeterministicValidationException($"JSON property '{name}' must be an integer.");
        }

        return result;
    }

    public static long Int64(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var result))
        {
            throw new DeterministicValidationException($"JSON property '{name}' must be an integer.");
        }

        return result;
    }

    public static JsonElement Object(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new DeterministicValidationException($"JSON property '{name}' must be an object.");
        }

        return value;
    }

    public static JsonElement? NullableObject(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            throw new DeterministicValidationException($"JSON property '{name}' is required.");
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Object => value,
            _ => throw new DeterministicValidationException(
                $"JSON property '{name}' must be an object or null.")
        };
    }

    public static JsonElement Array(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            throw new DeterministicValidationException($"JSON property '{name}' must be an array.");
        }

        return value;
    }

    public static IReadOnlyList<string> StringArray(JsonElement element, string name) =>
        Array(element, name).EnumerateArray().Select(item =>
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new DeterministicValidationException(
                    $"JSON property '{name}' must contain only strings.");
            }

            return item.GetString()!;
        }).ToArray();

    public static Sha256Digest Digest(JsonElement element)
    {
        RequireProperties(element, "algorithm", "value");
        var digest = new Sha256Digest(String(element, "algorithm"), String(element, "value"));
        BlazorComponentReadiness.Validator.Evidence.EvidenceIdentity.ValidateDigest(digest, "digest");
        return digest;
    }

    public static void WriteDigest(Utf8JsonWriter writer, string property, Sha256Digest digest)
    {
        writer.WritePropertyName(property);
        WriteDigest(writer, digest);
    }

    public static void WriteDigest(Utf8JsonWriter writer, Sha256Digest digest)
    {
        writer.WriteStartObject();
        writer.WriteString("algorithm", digest.Algorithm);
        writer.WriteString("value", digest.Value);
        writer.WriteEndObject();
    }

    public static Sha256Digest RawDigest(ReadOnlySpan<byte> bytes) =>
        new("sha256", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));

    public static void RequireCanonical(
        ReadOnlySpan<byte> original,
        ReadOnlySpan<byte> canonical,
        string resource)
    {
        if (!original.SequenceEqual(canonical))
        {
            throw new DeterministicValidationException(
                $"{resource} is not in canonical JSON property order and encoding.");
        }
    }

    public static string NormalizeText(string? value, string name, int maximumBytes, bool nullable = false)
    {
        if (value is null)
        {
            if (nullable)
            {
                return null!;
            }

            throw new DeterministicValidationException($"{name} is required.");
        }

        if (value.Length == 0 ||
            !value.IsNormalized(NormalizationForm.FormC) ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            Encoding.UTF8.GetByteCount(value) > maximumBytes ||
            value.Any(character => char.IsControl(character)))
        {
            throw new DeterministicValidationException(
                $"{name} must be trimmed NFC text without controls and within {maximumBytes} UTF-8 bytes.");
        }

        return value;
    }

    public static string? NormalizeOptionalText(string? value, string name, int maximumBytes)
    {
        if (value is null)
        {
            return null;
        }

        return NormalizeText(value, name, maximumBytes);
    }

    public static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        string.Equals(value.Trim(), "TBD", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value.Trim(), "TODO", StringComparison.OrdinalIgnoreCase);
}
