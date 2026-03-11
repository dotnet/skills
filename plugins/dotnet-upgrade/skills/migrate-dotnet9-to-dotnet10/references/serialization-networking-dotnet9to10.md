# Serialization and Networking Breaking Changes (.NET 10)

These changes affect projects using System.Text.Json, XmlSerializer, HttpClient, and networking APIs.

## Serialization

### System.Text.Json checks for property name conflicts

**Impact: Medium.** Polymorphic types with properties that conflict with metadata names (`$type`, `$id`, `$ref`, or custom `TypeDiscriminatorPropertyName`) now throw `InvalidOperationException` during serialization instead of producing invalid JSON.

```csharp
// This now throws InvalidOperationException at serialization time:
[JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
[JsonDerivedType(typeof(Dog), "dog")]
public abstract class Animal
{
    public abstract string Type { get; }  // Conflicts with "Type" discriminator
}

// Fix: Rename the property, or add [JsonIgnore]:
public abstract class Animal
{
    [JsonIgnore]
    public abstract string Type { get; }
}
```

### XmlSerializer no longer ignores properties marked with ObsoleteAttribute

**Security consideration:** Properties marked `[Obsolete]` are now included in XML serialization. Previously they were silently skipped. If obsolete properties contain sensitive data (e.g., deprecated password fields, legacy PII, or internal-only values), they will now appear in serialized output. Audit obsolete properties for sensitive data and mark them with `[XmlIgnore]` if they should not be serialized.

## Networking

### HTTP/3 support disabled by default with PublishTrimmed

**When `<PublishTrimmed>true</PublishTrimmed>` or `<PublishAot>true</PublishAot>` is set, HTTP/3 support is completely disabled by default.** The HTTP/3 code is stripped by the trimmer. This is NOT a native library search issue — the HTTP/3 implementation code itself is removed.

**Fix:** Add `<Http3Support>true</Http3Support>` to your `.csproj` to preserve HTTP/3 support when trimming:
```xml
<PropertyGroup>
  <PublishTrimmed>true</PublishTrimmed>
  <Http3Support>true</Http3Support>
</PropertyGroup>
```

### MailAddress enforces validation for consecutive dots

`MailAddress` now rejects email addresses with consecutive dots (e.g., `user..name@example.com`). Previously these were accepted.

### Streaming HTTP responses enabled by default in browser HTTP clients

In Blazor WebAssembly and other browser-based HTTP clients, streaming responses are now enabled by default. This may change how response content is buffered and consumed.

### Uri length limits removed

**Security consideration:** The `Uri` class no longer enforces length limits. Previously, very long URIs could throw exceptions. If your application relied on `Uri` to reject excessively long input (as an input validation or sanitization gate), this removal may expose denial-of-service or resource exhaustion attack surface. Add explicit length validation before constructing `Uri` instances from untrusted input.
