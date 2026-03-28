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

## When Not to Use

- The project requires Newtonsoft.Json features that System.Text.Json cannot support (extremely rare edge cases like `$ref/$id` with deep graphs)
- The user is already using System.Text.Json and just needs help with it
- The user explicitly wants to keep Newtonsoft.Json

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
| **Property naming** | As declared (typically PascalCase) | As declared (typically PascalCase) | Same ✓ (both preserve the property name as written in the class) |
| **Character escaping** | Only escapes characters required by JSON spec | **Escapes non-ASCII and HTML-sensitive characters** | Output looks different but is semantically equivalent; use `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` if unescaped output is needed (e.g., for readability), but understand the trade-offs: relaxed escaping is safe for API responses but may require additional escaping if the JSON is embedded in HTML |
| **Missing properties** | Ignored silently | Ignored silently | Same ✓ |
| **Extra JSON properties** | Ignored by default | Ignored by default (can opt-in to throw in .NET 8+) | Same ✓ (stricter behavior available via options) |
| **Trailing commas** | Allowed | **Rejected by default** | Parse errors on valid-looking JSON |
| **Comments in JSON** | Allowed | **Rejected by default** | Config files break |
| **Number in string** (`"123"`) | Coerced automatically | **Throws by default** | Deserialization breaks! |
| **Enum serialization** | Numeric by default | Numeric by default | Same ✓, but converter syntax differs |
| **null → non-nullable value type** | Sets to default(T) | Sets to default(T) | Same ✓ (null becomes default(T)) |
| **Case sensitivity** | Case-insensitive | **Case-sensitive by default** | Property matching breaks |
| **Max depth** | 64 | 64 | Same ✓ |
| **Circular references** | `$ref/$id` with PreserveReferencesHandling | `ReferenceHandler.Preserve` (.NET 5+) | API differs |

### Step 2: Configure System.Text.Json to match Newtonsoft.Json behavior

> **Security note:** Several settings below widen the parser's acceptance surface.
> System.Text.Json's stricter defaults are intentional security boundaries. Only enable
> the settings your application actually needs — do not blindly apply them all.

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
    // Case-insensitive matching (Newtonsoft default).
    // ⚠️ Enables multiple JSON properties to map to one .NET property,
    // which can cause interoperability issues. Only enable if needed.
    options.PropertyNameCaseInsensitive = true;

    // Allow numbers in string form like "123" (Newtonsoft coerces automatically).
    // ⚠️ Widens the accepted input surface — only enable if your data contains
    // quoted numbers and you cannot fix the producer.
    options.NumberHandling = JsonNumberHandling.AllowReadingFromString;

    options.ReadCommentHandling = JsonCommentHandling.Skip;     // Newtonsoft allows
    options.AllowTrailingCommas = true;                         // Newtonsoft allows

    // Enum string serialization (replaces StringEnumConverter)
    options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

    // Handle circular references — use IgnoreCycles to silently break cycles.
    // ⚠️ ReferenceHandler.Preserve emits $id/$ref metadata and significantly
    // increases the deserialization attack surface (an adversary who controls the
    // JSON can rewire object graph edges). Only use Preserve if you specifically
    // need round-trip reference identity and the JSON comes from a trusted source.
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

**Newtonsoft converter pattern:**
```csharp
// OLD: Newtonsoft.Json
public class UnixDateTimeConverter : Newtonsoft.Json.JsonConverter<DateTime>
{
    public override DateTime ReadJson(JsonReader reader, Type objectType,
        DateTime existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var timestamp = (long)reader.Value!;
        return DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
    }

    public override void WriteJson(JsonWriter writer, DateTime value,
        JsonSerializer serializer)
    {
        var timestamp = new DateTimeOffset(value).ToUnixTimeSeconds();
        writer.WriteValue(timestamp);
    }
}
```

**System.Text.Json converter pattern:**
```csharp
// NEW: System.Text.Json
public class UnixDateTimeConverter : System.Text.Json.Serialization.JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        var timestamp = reader.GetInt64(); // Note: strongly typed reader methods
        return DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value,
        JsonSerializerOptions options)
    {
        var timestamp = new DateTimeOffset(value).ToUnixTimeSeconds();
        writer.WriteNumberValue(timestamp);
    }
}
```

**Key differences in converter API:**
- Reader is `ref Utf8JsonReader` (struct, passed by ref) — NOT a class
- Writer is `Utf8JsonWriter` — write methods are `WriteStringValue`, `WriteNumberValue`, `WriteBooleanValue` (typed)
- No `serializer` parameter — use `options` and call `JsonSerializer.Serialize/Deserialize` for nested objects
- For polymorphic deserialization: use `JsonTypeInfo` and `[JsonDerivedType]` (.NET 7+) instead of custom type handling

### Step 5: Replace JToken/JObject/JArray with JsonNode

**Use `JsonNode` (System.Text.Json.Nodes) as the primary replacement for JToken/JObject/JArray.** It provides a mutable DOM that is the closest equivalent to Newtonsoft's LINQ-to-JSON:

```csharp
// Mutable DOM — replaces JObject/JArray patterns
var node = JsonNode.Parse(json)!;
node["newProperty"] = "value";           // Add/set properties
node["nested"] = new JsonObject          // Create nested objects
{
    ["key"] = 42
};
string name = (string)node["name"]!;     // Read values with cast
var result = node.ToJsonString();         // Serialize back
```

| Newtonsoft.Json | System.Text.Json (JsonNode) | Notes |
|----------------|----------------------------|-------|
| `JToken.Parse(json)` | `JsonNode.Parse(json)` | Returns mutable tree |
| `JObject obj = ...` | `JsonObject obj = ...` | Create with `new JsonObject { ... }` |
| `obj["key"]` | `node["key"]` | Returns `JsonNode?`; cast to get value |
| `obj["key"]?.Value<int>()` | `(int)node["key"]!` | Or use `.GetValue<int>()` |
| `obj.Add("key", value)` | `node["key"] = value` | Mutable — unlike JsonElement |

> **For high-performance read-only scenarios**, consider `JsonDocument`/`JsonElement` instead.
> `JsonDocument` is `IDisposable` and must be wrapped in `using`. `JsonElement` is a
> read-only struct that becomes invalid after the owning `JsonDocument` is disposed
> (clone with `element.Clone()` if needed).

### Step 6: Handle polymorphic serialization

**Newtonsoft.Json (uses $type discriminator):**
```csharp
// ⚠️ SECURITY RISK: TypeNameHandling allows an attacker to control the deserialized
// type, enabling remote code execution. Do NOT migrate this pattern as-is.
// System.Text.Json's approach below is secure by design (explicit allow-list).
var settings = new JsonSerializerSettings
{
    TypeNameHandling = TypeNameHandling.Auto // NEVER use with untrusted input!
};
```

**System.Text.Json (.NET 7+ — type discriminators):**
```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CreditCardPayment), typeDiscriminator: "credit")]
[JsonDerivedType(typeof(BankTransferPayment), typeDiscriminator: "bank")]
public abstract class Payment
{
    public decimal Amount { get; set; }
}

public class CreditCardPayment : Payment
{
    public string CardNumber { get; set; } = "";
}

// Serializes as: {"$type":"credit","amount":99.99,"cardNumber":"..."}
// System.Text.Json requires [JsonPolymorphic] on the base type and explicit
// [JsonDerivedType] for each allowed subtype — no arbitrary type instantiation.
```

### Step 7: Update package references

```xml
<!-- Remove from .csproj -->
<PackageReference Include="Newtonsoft.Json" Version="*" />
<PackageReference Include="Microsoft.AspNetCore.Mvc.NewtonsoftJson" Version="*" />

<!-- System.Text.Json is included in the framework — no package needed for .NET 6+ -->
<!-- Only add explicitly if you need a newer version: -->
<!-- <PackageReference Include="System.Text.Json" Version="8.0.0" /> -->
```

**Update using statements:**
```csharp
// Remove:
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json.Converters;

// Add:
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;  // For JsonNode (mutable DOM)
```

## Validation

- [ ] All `using Newtonsoft.Json` references removed
- [ ] All `[JsonProperty]` replaced with `[JsonPropertyName]`
- [ ] Custom converters use `System.Text.Json.Serialization.JsonConverter<T>` base
- [ ] `JObject`/`JToken` replaced with `JsonDocument` (read-only) or `JsonNode` (mutable)
- [ ] API responses match previous JSON format (property casing, null handling)
- [ ] Deserialization handles edge cases: trailing commas, comments, numbers-as-strings
- [ ] No `TypeNameHandling` equivalent (security improvement)
- [ ] `JsonDocument` usages wrapped in `using` statements

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Forgetting `PropertyNameCaseInsensitive = true` | Deserialization silently returns default values for all properties |
| `JsonDocument` not disposed | Memory leak — always `using var doc = JsonDocument.Parse(...)` |
| Using `JsonElement` after `JsonDocument` is disposed | JsonElement is invalid after dispose; clone with `element.Clone()` if needed |
| `[JsonIgnore]` from wrong namespace | Both Newtonsoft and System.Text.Json have `[JsonIgnore]` — wrong `using` = attribute ignored |
| Custom converter reading past the current token | System.Text.Json reader is strict — must read exactly the right tokens |
| `JsonExtensionData` type mismatch | Use `Dictionary<string, JsonElement>`, `IDictionary<string, object>`, or `JsonObject` — not `JToken` |
