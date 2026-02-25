```skill
---
name: migrating-newtonsoft-to-system-text-json
description: Migrate from Newtonsoft.Json to System.Text.Json, handling behavioral differences, custom converters, and common breaking changes. Use when converting a project from Newtonsoft.Json (Json.NET) to the built-in System.Text.Json serializer.
---

# Migrating from Newtonsoft.Json to System.Text.Json

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
| **Property naming** | PascalCase by default (as declared) | **PascalCase by default** | Same ✓ (unless you used a custom ContractResolver) |
| **Missing properties** | Ignored silently | Ignored silently | Same ✓ |
| **Extra JSON properties** | Ignored by default | Ignored by default (can opt-in to throw in .NET 8+) | Same ✓ (stricter behavior available via options) |
| **Trailing commas** | Allowed | **Rejected by default** | Parse errors on valid-looking JSON |
| **Comments in JSON** | Allowed | **Rejected by default** | Config files break |
| **Number in string** (`"123"`) | Coerced automatically | **Throws by default** | Deserialization breaks! |
| **Enum serialization** | Numeric by default | Numeric by default | Same ✓, but converter syntax differs |
| **null → non-nullable value type** | Sets to default(T) | **Throws exception** | Breaks on dirty data |
| **Case sensitivity** | Case-insensitive | **Case-sensitive by default** | Property matching breaks |
| **Max depth** | 64 | 64 | Same ✓ |
| **Circular references** | `$ref/$id` with PreserveReferencesHandling | `ReferenceHandler.Preserve` (.NET 5+) | API differs |

### Step 2: Configure System.Text.Json to match Newtonsoft.Json behavior

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
    // Match Newtonsoft.Json default behavior:
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase; // Newtonsoft default
    options.PropertyNameCaseInsensitive = true;                 // Newtonsoft default
    options.NumberHandling = JsonNumberHandling.AllowReadingFromString; // Newtonsoft coerces
    options.ReadCommentHandling = JsonCommentHandling.Skip;     // Newtonsoft allows
    options.AllowTrailingCommas = true;                         // Newtonsoft allows
    options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull; // Common Newtonsoft setting

    // Enum string serialization (replaces StringEnumConverter)
    options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

    // Handle circular references (replaces PreserveReferencesHandling)
    options.ReferenceHandler = ReferenceHandler.IgnoreCycles; // or Preserve for $ref/$id
}
```

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
| `[JsonExtensionData]` | `[JsonExtensionData]` + must be `Dictionary<string, JsonElement>` (NOT `JToken`) |

**Regex for finding Newtonsoft attributes:**
```bash
# Find all files using Newtonsoft attributes
grep -rn "using Newtonsoft.Json" --include="*.cs"
grep -rn "\[JsonProperty\|JsonConverter\|JsonIgnore\|JsonConstructor" --include="*.cs"
```

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

### Step 5: Replace JToken/JObject/JArray with JsonDocument/JsonElement

| Newtonsoft.Json | System.Text.Json | Notes |
|----------------|-------------------|-------|
| `JToken.Parse(json)` | `JsonDocument.Parse(json)` | **JsonDocument is IDisposable!** Must wrap in `using` |
| `JObject obj = ...` | `JsonElement obj = doc.RootElement` | JsonElement is a struct (no allocation) |
| `obj["key"]` | `obj.GetProperty("key")` | Throws if missing; use `TryGetProperty` for safe access |
| `obj["key"]?.Value<int>()` | `obj.GetProperty("key").GetInt32()` | Type-specific getters |
| `obj.Add("key", value)` | **Not possible** — JsonElement is read-only | Use `JsonNode` (System.Text.Json.Nodes) for mutable DOM |

**For mutable DOM operations, use JsonNode (NOT JsonDocument):**
```csharp
// Mutable DOM — replaces JObject/JArray mutation patterns
var node = JsonNode.Parse(json)!;
node["newProperty"] = "value";           // Add/set properties
node["nested"] = new JsonObject          // Create nested objects
{
    ["key"] = 42
};
var result = node.ToJsonString();        // Serialize back
```

### Step 6: Handle polymorphic serialization

**Newtonsoft.Json (uses $type discriminator):**
```csharp
var settings = new JsonSerializerSettings
{
    TypeNameHandling = TypeNameHandling.Auto // SECURITY RISK!
};
```

**System.Text.Json (.NET 7+ — type discriminators):**
```csharp
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
// Note: System.Text.Json uses "$type" by default (configurable)
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
| `JsonExtensionData` with `Dictionary<string, object>` | Must be `Dictionary<string, JsonElement>` — not `object` or `JToken` |
```
