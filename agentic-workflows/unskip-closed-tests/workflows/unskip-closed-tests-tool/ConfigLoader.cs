using System.Text.Json;

namespace UnskipClosedTests.Tool;

internal static class ConfigLoader
{
    private static readonly HashSet<string> AllowedProperties =
    [
        "schema_version",
        "source_roots",
        "excluded_globs",
        "generated_globs",
        "ignore_attribute_names",
        "test_attribute_names",
        "verification",
    ];

    public static ToolConfig Load(string path)
    {
        using JsonDocument document = JsonSupport.ReadDocument(path);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ContractException("Config root must be an object.");
        }

        RejectUnknownProperties(root, AllowedProperties, "config");
        ToolConfig config = new()
        {
            SchemaVersion = RequiredString(root, "schema_version"),
            SourceRoots = RequiredStringArray(root, "source_roots"),
            ExcludedGlobs = OptionalStringArray(root, "excluded_globs"),
            GeneratedGlobs = OptionalStringArray(root, "generated_globs"),
            IgnoreAttributeNames = RequiredStringArray(root, "ignore_attribute_names"),
            TestAttributeNames = RequiredStringArray(root, "test_attribute_names"),
        };

        if (!root.TryGetProperty("verification", out JsonElement verification) ||
            verification.ValueKind != JsonValueKind.Object)
        {
            throw new ContractException("verification must be an object.");
        }

        RejectUnknownProperties(verification, ["command", "timeout_seconds"], "verification");
        config.VerificationCommand = RequiredStringArray(verification, "command");
        config.VerificationTimeoutSeconds = RequiredPositiveInt(verification, "timeout_seconds");

        Validate(config);
        return config;
    }

    public static string Digest(ToolConfig config) => JsonSupport.CanonicalDigest(config);

    private static void Validate(ToolConfig config)
    {
        if (config.SchemaVersion != "1")
        {
            throw new ContractException($"Unsupported config schema_version '{config.SchemaVersion}'.");
        }

        if (config.SourceRoots.Count == 0)
        {
            throw new ContractException("source_roots must contain at least one path.");
        }

        if (config.IgnoreAttributeNames.Count == 0 || config.TestAttributeNames.Count == 0)
        {
            throw new ContractException("ignore_attribute_names and test_attribute_names must not be empty.");
        }

        if (config.VerificationCommand.Count == 0)
        {
            throw new ContractException("verification.command must not be empty.");
        }

        ValidateUniqueNonEmpty(config.SourceRoots, "source_roots");
        ValidateUniqueNonEmpty(config.ExcludedGlobs, "excluded_globs");
        ValidateUniqueNonEmpty(config.GeneratedGlobs, "generated_globs");
        ValidateUniqueNonEmpty(config.IgnoreAttributeNames, "ignore_attribute_names");
        ValidateUniqueNonEmpty(config.TestAttributeNames, "test_attribute_names");
        ValidateUniqueNonEmpty(config.VerificationCommand, "verification.command", requireUnique: false);

        foreach (string root in config.SourceRoots)
        {
            PathRules.ValidateRelativePath(root, "source root");
        }

        foreach (string glob in config.ExcludedGlobs.Concat(config.GeneratedGlobs))
        {
            if (Path.IsPathRooted(glob) || glob.Contains('\\'))
            {
                throw new ContractException($"Glob '{glob}' must be repository-relative and use '/' separators.");
            }

            if (glob.Split('/').Any(static segment => segment == ".."))
            {
                throw new ContractException($"Glob '{glob}' contains traversal.");
            }
        }

        foreach (string name in config.IgnoreAttributeNames.Concat(config.TestAttributeNames))
        {
            if (!IsAttributeName(name))
            {
                throw new ContractException($"Attribute name '{name}' is not a simple or qualified C# identifier.");
            }
        }
    }

    private static bool IsAttributeName(string value)
    {
        string[] pieces = value.Split('.');
        return pieces.Length > 0 && pieces.All(static piece =>
            piece.Length > 0 &&
            (char.IsLetter(piece[0]) || piece[0] == '_') &&
            piece.Skip(1).All(static character => char.IsLetterOrDigit(character) || character == '_'));
    }

    private static void ValidateUniqueNonEmpty(List<string> values, string name, bool requireUnique = true)
    {
        if (values.Any(static value => string.IsNullOrWhiteSpace(value)))
        {
            throw new ContractException($"{name} contains an empty value.");
        }

        if (requireUnique && values.Distinct(StringComparer.Ordinal).Count() != values.Count)
        {
            throw new ContractException($"{name} contains duplicate values.");
        }
    }

    private static void RejectUnknownProperties(JsonElement element, HashSet<string> allowed, string context)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new ContractException($"Unknown {context} property '{property.Name}'.");
            }
        }
    }

    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            throw new ContractException($"{name} must be a string.");
        }

        return value.GetString()!;
    }

    private static List<string> RequiredStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            throw new ContractException($"{name} is required.");
        }

        return ReadStringArray(value, name);
    }

    private static List<string> OptionalStringArray(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) ? ReadStringArray(value, name) : [];

    private static List<string> ReadStringArray(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new ContractException($"{name} must be an array.");
        }

        List<string> result = [];
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new ContractException($"{name} must contain only strings.");
            }

            result.Add(item.GetString()!);
        }

        return result;
    }

    private static int RequiredPositiveInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
        {
            throw new ContractException($"{name} is required.");
        }

        return ReadPositiveInt(value, name);
    }

    private static int ReadPositiveInt(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result) || result <= 0)
        {
            throw new ContractException($"{name} must be a positive 32-bit integer.");
        }

        return result;
    }
}
