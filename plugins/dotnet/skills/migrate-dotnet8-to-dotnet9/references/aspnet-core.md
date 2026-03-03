# ASP.NET Core 9 Breaking Changes

These changes affect projects using ASP.NET Core (Microsoft.NET.Sdk.Web).

## Behavioral Changes

### HostBuilder enables ValidateOnBuild/ValidateScopes in development

**Impact: Medium.** In the development environment, `ValidateOnBuild` and `ValidateScopes` are now enabled by default when options haven't been set with `UseDefaultServiceProvider`. This means DI registration issues that were previously silent will now throw at startup in development.

**Mitigation:** Fix the DI registration issues (recommended), or disable validation:
```csharp
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateOnBuild = false;
    options.ValidateScopes = false;
});
```

### Middleware types with multiple constructors

**Impact: Low.** Middleware activation behavior has changed when a middleware type has multiple constructors. Previously the behavior was undefined; now it selects the most appropriate constructor.

### Forwarded Headers Middleware ignores X-Forwarded-* headers from unknown proxies

**Impact: Medium.** This is a change backported from .NET 8. The `ForwardedHeadersMiddleware` now ignores `X-Forwarded-*` headers from proxies not in the `KnownProxies` or `KnownNetworks` list.

**Mitigation:** Add your trusted proxy addresses to `KnownProxies` or `KnownNetworks`:
```csharp
services.Configure<ForwardedHeadersOptions>(options =>
{
    options.KnownProxies.Add(IPAddress.Parse("10.0.0.1"));
});
```

> **Warning:** Do not clear `KnownProxies` or `KnownNetworks` to accept forwarded headers from any source. This disables ASP.NET Core's protection against spoofed `X-Forwarded-*` headers and is unsafe for production. Only register the specific proxy addresses or network ranges your infrastructure uses.

## Source-Incompatible Changes

### Legacy Mono and Emscripten APIs not exported to global namespace

**Impact: Low.** Legacy Mono and Emscripten interop APIs are no longer exported to the global namespace in Blazor WebAssembly. Use the specific namespace imports.

### DefaultKeyResolution.ShouldGenerateNewKey altered meaning

The meaning of `DefaultKeyResolution.ShouldGenerateNewKey` has changed. Review code that checks this property.

### Dev cert export no longer creates folder

`dotnet dev-certs https --export-path` no longer automatically creates the target folder. Ensure the directory exists before exporting.
