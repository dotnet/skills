using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace UnskipClosedTests.Tool;

internal static class JsonSupport
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    public static T Read<T>(string path)
    {
        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<T>(json, Options)
                ?? throw new ContractException($"JSON document '{path}' is empty.");
        }
        catch (ContractException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new ContractException($"Malformed JSON in '{path}': {ex.Message}");
        }
        catch (IOException ex)
        {
            throw new InfrastructureException($"Could not read '{path}'.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InfrastructureException($"Could not read '{path}'.", ex);
        }
    }

    public static JsonDocument ReadDocument(string path)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllBytes(path));
        }
        catch (JsonException ex)
        {
            throw new ContractException($"Malformed JSON in '{path}': {ex.Message}");
        }
        catch (IOException ex)
        {
            throw new InfrastructureException($"Could not read '{path}'.", ex);
        }
    }

    public static void Write<T>(string path, T value)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, JsonSerializer.Serialize(value, Options) + Environment.NewLine, new UTF8Encoding(false));
        }
        catch (IOException ex)
        {
            throw new InfrastructureException($"Could not write '{path}'.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InfrastructureException($"Could not write '{path}'.", ex);
        }
    }

    public static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string Sha256(string text) => Sha256(Encoding.UTF8.GetBytes(text));

    public static string CanonicalDigest<T>(T value, string? excludedProperty = null)
    {
        JsonNode node = JsonSerializer.SerializeToNode(value, Options)
            ?? throw new InfrastructureException("Could not serialize a digest input.");
        if (excludedProperty is not null && node is JsonObject root)
        {
            root.Remove(excludedProperty);
        }

        StringBuilder builder = new();
        WriteCanonical(node, builder);
        return Sha256(builder.ToString());
    }

    public static string ManifestDigest(Manifest manifest) =>
        CanonicalDigest(manifest, "manifest_digest");

    private static void WriteCanonical(JsonNode? node, StringBuilder builder)
    {
        switch (node)
        {
            case null:
                builder.Append("null");
                break;
            case JsonObject obj:
                builder.Append('{');
                bool firstProperty = true;
                foreach ((string key, JsonNode? value) in obj.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    if (!firstProperty)
                    {
                        builder.Append(',');
                    }

                    firstProperty = false;
                    builder.Append(JsonSerializer.Serialize(key));
                    builder.Append(':');
                    WriteCanonical(value, builder);
                }

                builder.Append('}');
                break;
            case JsonArray array:
                builder.Append('[');
                for (int i = 0; i < array.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(',');
                    }

                    WriteCanonical(array[i], builder);
                }

                builder.Append(']');
                break;
            case JsonValue value:
                builder.Append(value.ToJsonString());
                break;
            default:
                throw new InfrastructureException("Unsupported JSON node while computing a digest.");
        }
    }
}
