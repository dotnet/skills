---
name: migrating-newtonsoft-to-system-text-json
description: >
  Migrate from Newtonsoft.Json to System.Text.Json, handling behavioral differences,
  custom converters, and common breaking changes. Use when converting a project from
  Newtonsoft.Json (Json.NET) to the built-in System.Text.Json serializer.
---

# Migrating from Newtonsoft.Json to System.Text.Json

> **Important:** Migrating serializers is a nontrivial task. System.Text.Json will almost
> certainly behave differently from Newtonsoft.Json in subtle ways. Always validate
> serialization output and deserialization behavior thoroughly with real-world data after
> migrating. Automated and manual testing of all serialization paths is essential.

## When to Use

- Migrating an existing project from Newtonsoft.Json to System.Text.Json
- Removing the Newtonsoft.Json dependency for performance or AOT compatibility
- Fixing serialization differences after switching to System.Text.Json

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Code using Newtonsoft.Json | Yes | Models, serialization calls, custom converters |
| .NET version | No | Determines which System.Text.Json features are available |

## Workflow

### Step 1: Understand the critical behavioral differences

**System.Text.Json is NOT a drop-in replacement.** These behaviors differ by default:

| Behavior | Newtonsoft.Json | System.Text.Json | Impact |
|----------|----------------|-------------------|--------|
| **Character escaping** | Only escapes JSON-spec chars | **Escapes non-ASCII and HTML-sensitive** | Output differs but is equivalent; use `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` if needed |
| **Trailing commas** | Allowed | **Rejected by default** | Parse errors on valid-looking JSON |
| **Comments in JSON** | Allowed | **Rejected by default** | Config files break |
| **Number in string** (`"123"`) | Coerced automatically | **Throws by default** | Deserialization breaks! |
| **Case sensitivity** | Case-insensitive | **Case-sensitive by default** | Property matching breaks |
| **Circular references** | `$ref/$id` with PreserveReferencesHandling | `ReferenceHandler.Preserve` (.NET 5+) | API differs |

### Step 2: Configure System.Text.Json — start strict, loosen only what you need

> **Security-first approach:** System.Text.Json's stricter defaults are intentional
> security boundaries. Start from the strictest configuration and only loosen individual
> settings when your application specifically requires it. Interview the user about each
> compatibility requirement rather than applying a blanket compatibility configuration.

```csharp
// In Program.cs (ASP.NET Core) — configure globally
builder.Services.ConfigureHttpJsonOptions(options =>
{
    ConfigureJsonOptions(options.SerializerOptions);
});

// Also configure for controllers if using MVC
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        ConfigureJsonOptions(options.JsonSerializerOptions);
    });

static void ConfigureJsonOptions(JsonSerializerOptions options)
{
    // Start from strict defaults. Only add the settings below that your
    // application actually needs after reviewing the trade-offs.

    // ── Case sensitivity ──
    // Newtonsoft is case-insensitive by default; STJ is case-sensitive.
    // ⚠️ Case-insensitive matching enables multiple JSON properties to map to
    // one .NET property, which can cause interoperability/desync attacks.
    // Only enable if your JSON producers use inconsistent casing.
    // options.PropertyNameCaseInsensitive = true;

    // ── Numbers in strings ──
    // Newtonsoft coerces "123" to int automatically; STJ rejects by default.
    // ⚠️ Widens the accepted input surface. Only enable if your data contains
    // quoted numbers and you cannot fix the producer.
    // options.NumberHandling = JsonNumberHandling.AllowReadingFromString;

    // ── Comments ──
    // Newtonsoft allows comments; STJ rejects by default.
    // ⚠️ SECURITY: Allowing comments risks desynced deserialization attacks.
    // Different parsers disagree on where comments start/end (e.g., Newtonsoft,
    // JSON5, and STJ have different definitions of "end of line" for single-line
    // comments). An attacker can exploit these differences to smuggle values
    // through what appears to be an ignorable comment. Only enable for trusted
    // input like config files — never for user-supplied JSON.
    // options.ReadCommentHandling = JsonCommentHandling.Skip;

    // ── Trailing commas ──
    // Newtonsoft allows; STJ rejects by default. Low risk.
    // options.AllowTrailingCommas = true;

    // ── Enum string serialization ──
    // Replaces Newtonsoft's StringEnumConverter.
    options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

    // ── Circular references ──
    // Use IgnoreCycles to silently break cycles (safe default).
    // ⚠️ Do NOT use ReferenceHandler.Preserve unless you specifically need
    // round-trip reference identity AND the JSON comes from a trusted source.
    // Preserve emits $id/$ref metadata that lets an adversary rewire object
    // graph edges, potentially violating business logic.
    options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
}
```

> **Note:** `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull` is a common
> Newtonsoft.Json *configuration* but is NOT the Newtonsoft default (Json.NET includes
> nulls by default). Only add this if the existing Newtonsoft code was explicitly
> configured with `NullValueHandling.Ignore`.
>
> **Note:** Both serializers default to using the property name as declared (typically
> PascalCase). Only set `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` if the
> existing Newtonsoft code used `CamelCasePropertyNamesContractResolver` or the
> application specifically requires camelCase output.

### Step 3: Replace attribute mappings

| Newtonsoft.Json Attribute | System.Text.Json Equivalent |
|--------------------------|----------------------------|
| `[JsonProperty("name")]` | `[JsonPropertyName("name")]` |
| `[JsonIgnore]` | `[JsonIgnore]` (same name, different namespace!) |
| `[JsonProperty(Required = Required.Always)]` | `[JsonRequired]` (.NET 7+) |
| `[JsonProperty(NullValueHandling = NullValueHandling.Ignore)]` | `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` |
| `[JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]` | `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]` |
| `[JsonConverter(typeof(MyConverter))]` | `[JsonConverter(typeof(MyConverter))]` (different base class!) |
| `[JsonConstructor]` | `[JsonConstructor]` (same name, different namespace) |
| `[JsonExtensionData]` | `[JsonExtensionData]` — use `Dictionary<string, JsonElement>`, `IDictionary<string, object>`, or `JsonObject` (NOT `JToken`) |

### Step 4: Convert custom JsonConverters

Key API differences from Newtonsoft:
- Base class: `System.Text.Json.Serialization.JsonConverter<T>`
- Reader is `ref Utf8JsonReader` (struct by ref); Writer is `Utf8JsonWriter`
- Methods: `Read`/`Write` (not `ReadJson`/`WriteJson`)
- Typed write methods: `WriteStringValue`, `WriteNumberValue`, `WriteBooleanValue`
- Typed read methods: `reader.GetInt64()`, `reader.GetString()` (not casting `reader.Value`)
- Use `JsonSerializerOptions options` parameter (not `JsonSerializer serializer`)

### Step 5: Replace JToken/JObject/JArray with JsonNode

Use `JsonNode` (System.Text.Json.Nodes) for mutable DOM (replaces LINQ-to-JSON):
- `JToken.Parse` → `JsonNode.Parse`, `JObject` → `JsonObject`, `JArray` → `JsonArray`
- Read: `(string)node["key"]!` or `.GetValue<T>()`; Modify: `node["key"] = value`
- Serialize: `node.ToJsonString()`

For **read-only** scenarios, use `JsonDocument`/`JsonElement` (IDisposable, must clone if keeping past dispose).

### Step 6: Handle polymorphic serialization

⚠️ Newtonsoft's `TypeNameHandling` is a **security risk** (attacker-controlled type instantiation). System.Text.Json uses a secure-by-design approach (.NET 7+):
- `[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]` on base class
- `[JsonDerivedType(typeof(Subtype), typeDiscriminator: "name")]` for each allowed subtype
- No arbitrary type instantiation — explicit allow-list only

### Step 7: Update package references

Remove `Newtonsoft.Json` and `Microsoft.AspNetCore.Mvc.NewtonsoftJson` from .csproj. System.Text.Json is in-box for .NET 6+. Replace `using Newtonsoft.Json` / `Newtonsoft.Json.Linq` with `System.Text.Json` / `System.Text.Json.Serialization` / `System.Text.Json.Nodes`.

### Step 8: Write baseline serialization tests

Compare NJ and STJ output for every migrated model. Serialize with both, parse results to `JsonNode`, and assert equality. Also test round-trip deserialization for edge cases (nulls, extra properties, enums).

## Validation

- All `[JsonProperty]` replaced with `[JsonPropertyName]`
- Custom converters use `System.Text.Json.Serialization.JsonConverter<T>` base
- `JObject`/`JToken` replaced with `JsonNode` (mutable) or `JsonDocument` (read-only)
- API responses match previous format; deserialization handles edge cases

## Common Pitfalls

- Missing `PropertyNameCaseInsensitive = true` → deserialization silently returns defaults
- `[JsonIgnore]` from wrong namespace → attribute silently ignored (both NJ and STJ have it)
- `JsonDocument` not disposed → memory leak; always use `using`
- `JsonExtensionData` with `JToken` → use `Dictionary<string, JsonElement>` or `JsonObject`

## References

- [System.Text.Json overview](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview)
- [Migrate from Newtonsoft.Json](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/migrate-from-newtonsoft)
