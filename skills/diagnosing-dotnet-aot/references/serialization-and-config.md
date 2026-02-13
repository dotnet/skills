# Serialization and Configuration Patterns for AOT

Source-generated alternatives for reflection-based serialization and configuration binding.

## Contents
- [System.Text.Json Source Generation](#systemtextjson-source-generation) — JsonSerializerContext, polymorphic types, custom converters
- [Migrating from Newtonsoft.Json](#migrating-from-newtonsoftjson) — API mapping, behavioral differences
- [Configuration Binding Source Generator](#configuration-binding-source-generator) — EnableConfigurationBindingGenerator, options pattern
- [Options Validation Source Generator](#options-validation-source-generator) — OptionsValidator
- [Logging Source Generation](#logging-source-generation) — LoggerMessage

## System.Text.Json Source Generation

### Creating a JsonSerializerContext
🟡 **DO** | .NET 6+

Every type you serialize or deserialize must be registered in a `JsonSerializerContext`. The source generator produces optimized serialization code at compile time.

**Step 1: Create the context**
```csharp
[JsonSerializable(typeof(WeatherForecast))]
[JsonSerializable(typeof(List<WeatherForecast>))]
[JsonSerializable(typeof(ErrorResponse))]
internal partial class AppJsonContext : JsonSerializerContext { }
```

**Step 2: Use the context in serialization calls**
```csharp
// Serialize
string json = JsonSerializer.Serialize(forecast, AppJsonContext.Default.WeatherForecast);

// Deserialize
var forecast = JsonSerializer.Deserialize(json, AppJsonContext.Default.WeatherForecast);

// With HttpClient
var response = await httpClient.GetFromJsonAsync("/api/weather",
    AppJsonContext.Default.ListWeatherForecast);
```

**Step 3: Register context in ASP.NET Core minimal APIs**
```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});
```

### Handling Polymorphic Types
🟡 **DO** | .NET 7+

Use `[JsonDerivedType]` for polymorphic serialization instead of runtime type discovery.

❌
```csharp
// Runtime type discovery — not AOT compatible
JsonSerializer.Serialize<object>(derivedObj);
```
✅
```csharp
[JsonDerivedType(typeof(Cat), typeDiscriminator: "cat")]
[JsonDerivedType(typeof(Dog), typeDiscriminator: "dog")]
public class Animal { public string Name { get; set; } }

[JsonSerializable(typeof(Animal))]
internal partial class AppJsonContext : JsonSerializerContext { }
```

### Custom Converters with Source Generation
🟡 **DO** | .NET 8+

Custom `JsonConverter<T>` implementations work with source generation, but they must be registered on `JsonSerializerOptions` or applied via attributes — not discovered via reflection.

```csharp
[JsonConverter(typeof(DateOnlyConverter))]
public record Event(string Name, DateOnly Date);

public class DateOnlyConverter : JsonConverter<DateOnly>
{
    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) => DateOnly.Parse(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, DateOnly value,
        JsonSerializerOptions options) => writer.WriteStringValue(value.ToString("O"));
}
```

### Caching JsonSerializerOptions
🔴 **DO** | .NET 5+

Always cache `JsonSerializerOptions` — creating a new instance per call re-generates metadata (592x slower in .NET 6).

❌
```csharp
JsonSerializer.Serialize(obj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
```
✅
```csharp
private static readonly JsonSerializerOptions s_options = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    TypeInfoResolver = AppJsonContext.Default
};
JsonSerializer.Serialize(obj, s_options);
```

## Migrating from Newtonsoft.Json

### Common Migration Patterns

| Newtonsoft.Json | System.Text.Json Equivalent |
|----------------|----------------------------|
| `JsonConvert.SerializeObject(obj)` | `JsonSerializer.Serialize(obj, AppJsonContext.Default.MyType)` |
| `JsonConvert.DeserializeObject<T>(json)` | `JsonSerializer.Deserialize(json, AppJsonContext.Default.MyType)` |
| `[JsonProperty("name")]` | `[JsonPropertyName("name")]` |
| `[JsonIgnore]` | `[JsonIgnore]` (same attribute name, different namespace) |
| `JsonSerializerSettings` | `JsonSerializerOptions` (cache as static) |
| `JObject.Parse(json)` | `JsonDocument.Parse(json)` or `JsonNode.Parse(json)` |
| `JToken` navigation | `JsonNode` / `JsonElement` navigation |
| `DefaultValueHandling.Ignore` | `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault` |
| `NullValueHandling.Ignore` | `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull` |
| `ReferenceLoopHandling.Ignore` | `ReferenceHandler = ReferenceHandler.IgnoreCycles` (.NET 6+) |

### Key Behavioral Differences

- STJ is case-sensitive by default (Newtonsoft is case-insensitive). Use `PropertyNameCaseInsensitive = true` if needed.
- STJ does not serialize fields by default. Use `IncludeFields = true` if needed.
- STJ requires `[JsonConstructor]` for parameterized constructors (Newtonsoft auto-detects).

## Configuration Binding Source Generator

### Enabling the Generator
🟡 **DO** | .NET 8+

```xml
<PropertyGroup>
  <EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>
</PropertyGroup>
```

The source generator intercepts calls to `Bind()`, `Get<T>()`, and `Configure<T>()` and replaces them with compile-time generated code. Your C# code does not need to change.

### Options Pattern with Source-Generated Binding

```csharp
public class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 25;
    public bool UseSsl { get; set; }
}

// Registration — works with source generator
builder.Services.AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection("Smtp"));
```

### Limitations of the Config Binding Generator

- Properties must be public with `get`/`set` — `init`-only setters are not supported.
- Types must have a public parameterless constructor.
- Complex converters (`TypeConverter` patterns) may not be source-generated.

## Options Validation Source Generator

### Source-Generated Validation
🟡 **DO** | .NET 8+

Use `[OptionsValidator]` to generate validation code at compile time instead of reflection-based `ValidateDataAnnotations()`.

❌
```csharp
builder.Services.AddOptions<MyOptions>()
    .Bind(config.GetSection("My"))
    .ValidateDataAnnotations(); // uses reflection
```
✅
```csharp
[OptionsValidator]
internal sealed partial class MyOptionsValidator : IValidateOptions<MyOptions> { }

builder.Services.AddOptions<MyOptions>()
    .Bind(config.GetSection("My"));
builder.Services.AddSingleton<IValidateOptions<MyOptions>, MyOptionsValidator>();
```

### Supported Validation Attributes

The source generator supports `System.ComponentModel.DataAnnotations` attributes:
- `[Required]`, `[Range]`, `[MinLength]`, `[MaxLength]`, `[RegularExpression]`
- `[StringLength]`, `[EmailAddress]`, `[Url]`, `[Phone]`
- Custom `ValidationAttribute` subclasses (if the logic doesn't use reflection)

## Logging Source Generation

### LoggerMessage Source Generator
🟡 **DO** | .NET 6+

Use `[LoggerMessage]` to generate high-performance, AOT-compatible logging methods. Avoids boxing, string formatting, and reflection.

❌
```csharp
_logger.LogInformation("Processing order {OrderId} for {Amount}", orderId, amount);
// boxes value types, parses template at runtime
```
✅
```csharp
public static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Processing order {OrderId} for {Amount}")]
    public static partial void ProcessingOrder(ILogger logger, int orderId, decimal amount);
}

// Usage
Log.ProcessingOrder(_logger, orderId, amount);
```
**Impact: Zero boxing, compile-time template parsing, AOT-compatible.**
