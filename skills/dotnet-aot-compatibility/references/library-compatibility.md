# .NET Library AOT Compatibility Status

AOT compatibility status of commonly used .NET libraries and frameworks as of .NET 10 (2026).
All recommendations stay within the .NET ecosystem.

> **Key**: ✅ Fully compatible | ⚠️ Partially compatible / experimental | 🔴 Not compatible

## ASP.NET Core

| Feature | AOT Status | Notes |
|---------|:---:|-------|
| Minimal APIs | ✅ | Full support. Use `webapiaot` project template. |
| gRPC | ✅ | Fully supported with `Grpc.AspNetCore`. |
| Worker Services | ✅ | `BackgroundService` and `IHostedService` work. |
| Static Files | ✅ | Middleware works in AOT. |
| CORS | ✅ | Fully supported. |
| Health Checks | ✅ | Fully supported. |
| Rate Limiting | ✅ | Fully supported. |
| Output Caching | ✅ | Fully supported. |
| HTTP Logging | ✅ | Fully supported. |
| SignalR | ⚠️ | Partial support. Test thoroughly — some serialization and DI patterns may not work. |
| MVC Controllers | 🔴 | Not supported. Heavy reflection for model binding, action discovery, view activation. |
| Razor Pages | 🔴 | Not supported. Requires runtime compilation model. |
| Blazor Server | 🔴 | Not supported. |
| Blazor WebAssembly | ⚠️ | Uses Mono AOT (separate from NativeAOT), not covered by this skill. |

### Minimal APIs — AOT Configuration

```csharp
var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});

var app = builder.Build();
app.MapGet("/api/weather", () => Results.Ok(GetForecast()));
app.Run();

[JsonSerializable(typeof(WeatherForecast[]))]
internal partial class AppJsonContext : JsonSerializerContext { }
```

### If Your App Uses MVC Controllers

MVC is not compatible with Native AOT. Your options within .NET:
1. **Migrate API controllers to Minimal APIs** — most REST APIs can be expressed as minimal API endpoints with the same functionality
2. **Keep MVC and don't use AOT** — MVC apps work with standard JIT and ReadyToRun deployment
3. **Hybrid approach** — separate your API endpoints (minimal APIs, AOT-published) from your MVC/Razor UI (JIT-published) into different services

## Entity Framework Core

| Feature | AOT Status | Notes |
|---------|:---:|-------|
| Compiled Models | ⚠️ | Required for AOT. Generate with `dotnet ef dbcontext optimize`. |
| Precompiled Queries | ⚠️ | Experimental. Use `--precompile-queries --nativeaot` flag. |
| Runtime Model Building | 🔴 | Not supported in AOT. Throws at runtime without compiled model. |
| Migrations (runtime) | 🔴 | Not supported. Run migrations from a separate non-AOT project. |
| LINQ Queries | ⚠️ | Static LINQ queries work with precompilation. Dynamic query building may fail. |

### EF Core AOT Setup (Experimental)

```bash
# Generate compiled model and precompiled queries
dotnet ef dbcontext optimize --precompile-queries --nativeaot
```

```xml
<!-- Or use MSBuild integration -->
<PackageReference Include="Microsoft.EntityFrameworkCore.Tasks" Version="10.0.0" />
```

**Important limitations:**
- Compiled models must be regenerated when the model changes
- Not all query patterns can be precompiled — dynamic queries may fall back to interpreted expressions
- **This is experimental and not recommended for production** — test thoroughly
- Migrations must run from a separate JIT-compiled project or tool

### If EF Core AOT Doesn't Meet Your Needs

Stay within .NET but consider:
1. **ADO.NET directly** — `SqlConnection`, `SqlCommand`, `DbDataReader` are fully AOT-compatible
2. **Dapper** — check current AOT compatibility status; some features use reflection
3. **Thin repository pattern** — wrap ADO.NET in a repository abstraction for testability

## System.Text.Json

| Feature | AOT Status | Notes |
|---------|:---:|-------|
| Source-generated serialization | ✅ | Full support via `[JsonSerializable]` and `JsonSerializerContext`. |
| Reflection-based serialization | 🔴 | Not compatible. Produces IL2026 + IL3050 warnings. |
| `JsonDocument` / `JsonElement` | ✅ | DOM-based parsing works. |
| `JsonNode` | ✅ | Mutable DOM works. |
| Custom converters | ✅ | Work when registered via attributes or options, not discovered by reflection. |
| Polymorphic serialization | ✅ | Use `[JsonDerivedType]` (.NET 7+). |

## Microsoft.Extensions.*

| Library | AOT Status | Notes |
|---------|:---:|-------|
| DependencyInjection | ✅ | Built-in container works. Avoid assembly scanning. |
| Configuration | ✅ | With `EnableConfigurationBindingGenerator` (.NET 8+). |
| Logging | ✅ | With `[LoggerMessage]` source generator (.NET 6+). |
| Options | ✅ | With `[OptionsValidator]` source generator (.NET 8+). |
| Http (HttpClientFactory) | ✅ | Fully supported. |
| Caching (IMemoryCache) | ✅ | Fully supported. |
| Resilience (Polly v8+) | ✅ | Polly 8+ is AOT-compatible. |

## Networking and Communication

| Library | AOT Status | Notes |
|---------|:---:|-------|
| HttpClient | ✅ | Fully supported. Use `IHttpClientFactory` for lifecycle management. |
| gRPC (Grpc.Net.Client) | ✅ | Fully supported with protobuf source generation. |
| StackExchange.Redis | ⚠️ | Core functionality works. LuaScript parameter passing uses reflection (annotated `[RequiresUnreferencedCode]`). |

## Serialization

| Library | AOT Status | Notes |
|---------|:---:|-------|
| System.Text.Json (source gen) | ✅ | Recommended for all AOT apps. |
| Newtonsoft.Json | 🔴 | Fundamentally reflection-based. Will not be updated for AOT. Migrate to System.Text.Json. |
| protobuf-net | ⚠️ | Check latest version for AOT annotations. Core serialization may work with manual configuration. |
| MessagePack-CSharp | ⚠️ | Source generator mode available. Check latest release for AOT status. |
| System.Xml.Serialization | 🔴 | Reflection-based. Use `XmlReader`/`XmlWriter` directly for AOT. |
| BinaryFormatter | 🔴 | Removed in .NET 9. |

## Observability

| Library | AOT Status | Notes |
|---------|:---:|-------|
| OpenTelemetry (core) | ✅ | Made AOT-compatible in 2023. HttpClient and ASP.NET Core instrumentation work. |
| OpenTelemetry SqlClient instrumentation | 🔴 | Marked `[RequiresUnreferencedCode]` — underlying SqlClient not AOT compatible. |
| EventSource / EventPipe | ⚠️ | Requires `<EventSourceSupport>true</EventSourceSupport>`. Not all runtime events supported. |
| dotnet-trace / dotnet-counters | ⚠️ | Work with EventPipe support enabled. |
| Heap analysis (dotnet-gcdump) | 🔴 | Not supported in Native AOT. |

## Authentication and Identity

| Library | AOT Status | Notes |
|---------|:---:|-------|
| Microsoft.IdentityModel.JsonWebTokens | ✅ | Migrated from Newtonsoft.Json to System.Text.Json for AOT. |
| ASP.NET Core Authentication | ⚠️ | JWT Bearer works with minimal APIs. Cookie auth may need testing. |

## Desktop UI Frameworks

| Framework | AOT Status | Notes |
|-----------|:---:|-------|
| WPF | 🔴 | Heavy reflection usage. Not AOT-compatible. |
| Windows Forms | 🔴 | Relies on built-in COM marshalling. Not AOT-compatible. |
| .NET MAUI | ⚠️ | AOT support on iOS/Mac Catalyst. Requires trim and AOT-compatible code. XAML must be ahead-of-time compiled. |
| Avalonia UI | ⚠️ | Check latest version for AOT support status. |

## Evaluating Unlisted Libraries

For libraries not listed here, follow this process:

1. **Check for `IsAotCompatible` or `IsTrimmable` in the library's project file** — indicates the author has tested for AOT
2. **Check the library's NuGet page or GitHub README** for AOT compatibility notes
3. **Search the library's GitHub issues** for "AOT", "trimming", or "NativeAOT"
4. **Create a test project**: reference the library, set `<PublishAot>true</PublishAot>`, and run `dotnet publish -r <RID>`. Any warnings indicate potential issues
5. **Set `<TrimmerSingleWarn>false</TrimmerSingleWarn>`** to see individual warnings instead of one warning per assembly
