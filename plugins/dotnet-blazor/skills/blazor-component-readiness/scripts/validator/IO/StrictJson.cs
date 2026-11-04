using System.Buffers;
using System.Text;
using System.Text.Json;

namespace BlazorComponentReadiness.Validator.IO;

public static class StrictJson
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public static JsonDocument Parse(ReadOnlyMemory<byte> bytes, long maximumBytes, string resource)
    {
        BoundedIO.EnsureLength(bytes.Length, maximumBytes, resource);
        if (bytes.Span.StartsWith(Utf8Bom))
        {
            throw new DeterministicValidationException($"{resource} must be UTF-8 without a byte-order mark.");
        }

        try
        {
            _ = StrictUtf8.GetString(bytes.Span);
            var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });
            RejectDuplicateProperties(document.RootElement, "$", resource);
            return document;
        }
        catch (DecoderFallbackException exception)
        {
            throw new DeterministicValidationException($"{resource} is not strict UTF-8.", exception);
        }
        catch (JsonException exception)
        {
            throw new DeterministicValidationException($"{resource} is not strict JSON: {exception.Message}", exception);
        }
    }

    public static JsonDocument Read(string path, long maximumBytes, string resource) =>
        Parse(BoundedIO.ReadAllBytes(path, maximumBytes, resource), maximumBytes, resource);

    public static byte[] Canonicalize(ReadOnlyMemory<byte> bytes, long maximumBytes, string resource)
    {
        using var document = Parse(bytes, maximumBytes, resource);
        return SerializeCanonical(document.RootElement.WriteTo);
    }

    public static byte[] SerializeCanonical(Action<Utf8JsonWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false
        }))
        {
            write(writer);
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void RejectDuplicateProperties(JsonElement element, string path, string resource)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw new DeterministicValidationException(
                            $"{resource} contains duplicate property '{property.Name}' at {path}.");
                    }

                    RejectDuplicateProperties(property.Value, $"{path}.{property.Name}", resource);
                }

                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    RejectDuplicateProperties(item, $"{path}[{index}]", resource);
                    index++;
                }

                break;
        }
    }
}
