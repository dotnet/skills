---
name: system-text-json-net11
description: >
  Provides guidance on new System.Text.Json APIs introduced in .NET 11.
  It covers typed JsonTypeInfo access via GetTypeInfo<T> and TryGetTypeInfo<T> on
  JsonSerializerOptions, and the new JsonNamingPolicy.PascalCase static property.
  Use when serializing or deserializing JSON in .NET 11 applications and needing
  typed metadata access or PascalCase property naming.
license: MIT
---

# System.Text.Json — .NET 11

New APIs added to `System.Text.Json` across .NET 11 releases.

## When to Use

- Serializing or deserializing JSON in a .NET 11 (or later) project
- Needing strongly-typed `JsonTypeInfo<T>` access instead of the untyped `JsonTypeInfo` overload
- Wanting to safely check whether type metadata is available without catching exceptions (`TryGetTypeInfo<T>`)
- Requiring PascalCase property naming during JSON serialization

## When Not to Use

- The project targets .NET 10 or earlier — these APIs are not available before .NET 11
- Using a JSON library that is not `System.Text.Json` (e.g., Newtonsoft.Json)
- The existing untyped `GetTypeInfo(Type)` / `TryGetTypeInfo(Type, ...)` overloads are sufficient

## Target Framework

```xml
<TargetFramework>net11.0</TargetFramework>
```

## New APIs

### Typed `JsonTypeInfo` Access

#### `JsonSerializerOptions.GetTypeInfo<T>()`

Returns a strongly-typed `JsonTypeInfo<T>` for the specified type, using the
options' configured type-info resolver.

```csharp
JsonTypeInfo<T> GetTypeInfo<T>()
```

#### `JsonSerializerOptions.TryGetTypeInfo<T>(out JsonTypeInfo<T>?)`

Attempts to retrieve typed metadata without throwing if the type is not resolved.

```csharp
bool TryGetTypeInfo<T>(out JsonTypeInfo<T>? typeInfo)
```

### `JsonNamingPolicy.PascalCase`

A new static property that converts property names to PascalCase during
serialization.

```csharp
static JsonNamingPolicy PascalCase { get; }
```

## Examples

### Get Typed JsonTypeInfo

```csharp
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

// Retrieve strongly-typed metadata for MyClass
JsonTypeInfo<MyClass> typeInfo = options.GetTypeInfo<MyClass>();
Console.WriteLine($"Type: {typeInfo.Type.Name}");
```

### TryGetTypeInfo for Safe Access

```csharp
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

if (options.TryGetTypeInfo<MyClass>(out var info))
{
    Console.WriteLine($"Resolved type info for {info!.Type.Name}");
}
else
{
    Console.WriteLine("Type info not available");
}
```

### PascalCase Naming Policy

```csharp
using System.Text.Json;

var opts = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.PascalCase
};

var obj = new { firstName = "John", lastName = "Doe" };
string json = JsonSerializer.Serialize(obj, opts);
Console.WriteLine(json);
// Output: {"FirstName":"John","LastName":"Doe"}
```

### Combined: Serialize with Typed Metadata

```csharp
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.PascalCase
};

JsonTypeInfo<Person> typeInfo = options.GetTypeInfo<Person>();
string json = JsonSerializer.Serialize(new Person("Jane", 30), typeInfo);
Console.WriteLine(json);
// Output: {"Name":"Jane","Age":30}

public record Person(string Name, int Age);
```
