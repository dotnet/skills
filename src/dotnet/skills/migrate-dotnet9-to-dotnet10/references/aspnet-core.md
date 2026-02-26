# ASP.NET Core 10 Breaking Changes

These changes affect projects using ASP.NET Core (Microsoft.NET.Sdk.Web).

## Source-Incompatible Changes

### WebHostBuilder, IWebHost, and WebHost are obsolete

The legacy `WebHostBuilder` and related APIs are now marked obsolete. Migrate to the modern hosting model:

```csharp
// Before (.NET 9)
var host = new WebHostBuilder()
    .UseKestrel()
    .UseStartup<Startup>()
    .Build();

// After (.NET 10)
var builder = WebApplication.CreateBuilder(args);
// Configure services in builder.Services
var app = builder.Build();
// Configure middleware pipeline
app.Run();
```

If still using `Startup` classes, the `WebApplication` model supports them via `builder.Host.ConfigureWebHostDefaults(...)` or inline configuration.

### IActionContextAccessor and ActionContextAccessor are obsolete

These types are obsolete. Access `ActionContext` through dependency injection or the `HttpContext` instead:
```csharp
// Before
services.AddSingleton<IActionContextAccessor, ActionContextAccessor>();

// After — use IHttpContextAccessor or inject ActionContext directly in filters/middleware
```

### Deprecation of WithOpenApi extension method

The `WithOpenApi()` extension method is deprecated. Use the built-in OpenAPI document generation in ASP.NET Core 10 instead.

### IncludeOpenAPIAnalyzers property and MVC API analyzers deprecated

The `<IncludeOpenAPIAnalyzers>` MSBuild property is deprecated. Remove it from `.csproj` files. The analyzers are no longer needed with the new OpenAPI infrastructure.

### IPNetwork and ForwardedHeadersOptions.KnownNetworks are obsolete

`IPNetwork` is obsolete. Use `System.Net.IPNetwork` (the new runtime type) instead:
```csharp
// Before
options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("10.0.0.0"), 8));

// After — use the new System.Net.IPNetwork type
```

### Razor runtime compilation is obsolete

`AddRazorRuntimeCompilation()` is obsolete. Razor views and pages should be precompiled. For development, use hot reload (`dotnet watch`) instead.

### Microsoft.Extensions.ApiDescription.Client package deprecated

The `Microsoft.Extensions.ApiDescription.Client` package is deprecated. Use the built-in OpenAPI client generation tooling instead.

## Behavioral Changes

### Cookie login redirects disabled for known API endpoints

ASP.NET Core no longer redirects to login pages for requests to known API endpoints (e.g., those returning `ProblemDetails`). Instead, a `401` status code is returned directly.

This is generally the desired behavior for APIs, but may affect apps that relied on the redirect for API calls.

### Exception diagnostics suppressed when TryHandleAsync returns true

When `IExceptionHandler.TryHandleAsync` returns `true`, the exception diagnostics middleware no longer emits diagnostic events for that exception. If you rely on diagnostics (e.g., logging, telemetry) for handled exceptions, emit them within your exception handler.
