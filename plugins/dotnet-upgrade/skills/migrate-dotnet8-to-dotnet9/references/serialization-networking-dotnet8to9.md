# Serialization and Networking Breaking Changes (.NET 9)

These changes affect projects using BinaryFormatter, System.Text.Json, HttpClient, and networking APIs.

## Serialization

### BinaryFormatter always throws

**Impact: High.** The in-box `BinaryFormatter` implementation now always throws `NotSupportedException` on `Serialize` and `Deserialize`, even with settings that previously enabled its use. The `EnableUnsafeBinaryFormatterSerialization` AppContext switch has been removed.

```csharp
// Serialization — BREAKS at runtime with NotSupportedException
var formatter = new BinaryFormatter();
formatter.Serialize(stream, obj); // throws
```

```csharp
// Deserialization — BREAKS at runtime with NotSupportedException
var formatter = new BinaryFormatter();
var obj = formatter.Deserialize(stream); // throws
```

**This is a major architectural decision.** If the project uses BinaryFormatter, the agent should stop and ask the user which replacement to use. Options include:
1. **System.Text.Json** — Modern, high-performance, built-in
2. **MessagePack** or **protobuf-net** — Compact binary formats
3. **XmlSerializer** or **DataContractSerializer** — XML-based formats

> **Security: Do not re-enable BinaryFormatter.** A NuGet compatibility package exists (`System.Runtime.Serialization.Formatters`) but it is unsupported and **inherently unsafe** — BinaryFormatter deserialization enables remote code execution from crafted payloads, which is why it was removed. Do not suggest or use this package.

The choice depends on existing data format, backward compatibility requirements, and performance needs. Stored data may need a migration strategy.

See the [BinaryFormatter migration guide](https://learn.microsoft.com/en-us/dotnet/standard/serialization/binaryformatter-migration-guide/) for detailed guidance.

### Nullable JsonDocument properties deserialize to JsonValueKind.Null

**Impact: Medium.** Deserializing JSON `null` into `JsonDocument` now returns a non-null `JsonDocument` with `RootElement.ValueKind == JsonValueKind.Null` instead of returning C# `null`.

```csharp
var doc = JsonSerializer.Deserialize<JsonDocument>("null");
// .NET 8: doc is null
// .NET 9: doc is not null, doc.RootElement.ValueKind == JsonValueKind.Null
```

**Fix:** Update code that checks `doc is null` to also check `doc.RootElement.ValueKind`:
```csharp
if (doc is null || doc.RootElement.ValueKind == JsonValueKind.Null)
{
    // handle null
}
```

### System.Text.Json metadata reader now unescapes metadata property names

**Impact: Low.** The JSON metadata reader now unescapes metadata property names (like `$type`, `$id`). This may affect custom converters or code that processes raw JSON metadata.

## Networking

### HttpClientFactory uses SocketsHttpHandler as primary handler

**Impact: Medium.** The default primary handler for `HttpClientFactory`-created clients is now `SocketsHttpHandler` instead of `HttpClientHandler` on platforms that support it. Code that casts the handler to `HttpClientHandler` will throw `InvalidCastException`.

```csharp
// BREAKS — InvalidCastException at runtime in .NET 9
services.AddHttpClient("test")
    .ConfigureHttpMessageHandlerBuilder(b =>
    {
        ((HttpClientHandler)b.PrimaryHandler).UseCookies = false; // throws
    });
```

**Fix options:**
```csharp
// Option 1: Explicitly configure a primary handler
services.AddHttpClient("test")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler() { UseCookies = false });

// Option 2: Check for both handler types
services.AddHttpClient("test")
    .ConfigureHttpMessageHandlerBuilder(b =>
    {
        if (b.PrimaryHandler is HttpClientHandler hch) hch.UseCookies = false;
        else if (b.PrimaryHandler is SocketsHttpHandler shh) shh.UseCookies = false;
    });

// Option 3: Set defaults for all clients
services.ConfigureHttpClientDefaults(b =>
    b.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler() { UseCookies = false }));
```

`SocketsHttpHandler` also has `PooledConnectionLifetime` preset to match `HandlerLifetime`, improving DNS rotation for captured clients.

### HttpClientFactory logging redacts header values by default

**Impact: Medium.** All header values in `Trace`-level `HttpClientFactory` logs are now redacted by default. Previously, unspecified headers were logged in full.

**Fix:** Explicitly whitelist specific non-sensitive headers that need to be logged:
```csharp
// Allow specific non-sensitive headers to be logged unredacted
services.ConfigureHttpClientDefaults(b =>
    b.RedactLoggedHeaders(h => h != "Cache-Control" && h != "Accept"));
```

> **Warning:** Do not disable redaction globally with `RedactLoggedHeaders(_ => false)`. This logs all header values in cleartext, including `Authorization` tokens, cookies, and other credentials. Logs are often broadly accessible or exported to external systems, making this a credential leak risk.

### HttpClient metrics report `server.port` unconditionally

`HttpClient` metrics now always include the `server.port` attribute, even for default ports (80/443). This may affect metric dashboards or alerting rules.

### HttpListenerRequest.UserAgent is nullable

`HttpListenerRequest.UserAgent` is now `string?` instead of `string`. Add null checks.

### URI query redaction in HttpClient EventSource events

URI query strings are now redacted in `HttpClient` EventSource events for security.

### URI query redaction in IHttpClientFactory logs

URI query strings are now redacted in `IHttpClientFactory` logs for security.
