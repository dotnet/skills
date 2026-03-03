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

### Microsoft.OpenApi 2.x breaking API changes

`Microsoft.AspNetCore.OpenApi 10.0` depends on `Microsoft.OpenApi` v2.x (from [`microsoft/OpenAPI.NET`](https://github.com/microsoft/OpenAPI.NET) repo), which has significant breaking API changes from v1.x. Projects that customize OpenAPI document generation using transformers or directly manipulate OpenAPI models will need code changes. Note: no official v1→v2 migration guide exists; the repo has a [v3 upgrade guide](https://github.com/microsoft/OpenAPI.NET/blob/main/docs/upgrade-guide-3.md) only. These changes are also not listed on the ASP.NET Core 10 breaking changes page.

**Namespace changes — `Microsoft.OpenApi.Models` and `Microsoft.OpenApi.Any` are completely removed:**

All types previously in these namespaces (`OpenApiSchema`, `OpenApiParameter`, `OpenApiResponse`, `OpenApiSecurityScheme`, etc.) have moved to the root `Microsoft.OpenApi` namespace. Replace all `using Microsoft.OpenApi.Models;` and `using Microsoft.OpenApi.Any;` with `using Microsoft.OpenApi;`.

```csharp
// Before (.NET 9 — Microsoft.OpenApi v1.x)
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

// After (.NET 10 — Microsoft.OpenApi v2.x)
using Microsoft.OpenApi;          // ALL model types are here now
using System.Text.Json.Nodes;     // replaces OpenApiAny types
```

**Key API changes:**

1. **`OpenApiString` / `OpenApiAny` types removed** — Use `System.Text.Json.Nodes.JsonNode` instead:
   ```csharp
   // Before
   parameter.Schema.Example = new OpenApiString("1.0");
   // After
   parameter.Schema.Example = JsonNode.Parse("\"1.0\"");
   ```

2. **`OpenApiSecurityScheme.Reference` replaced** — Use `OpenApiSecuritySchemeReference` directly:
   ```csharp
   // Before
   var scheme = new OpenApiSecurityScheme
   {
       Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "oauth2" }
   };
   // After
   var scheme = new OpenApiSecuritySchemeReference("oauth2", null);
   ```

3. **Collections now nullable** — `operation.Parameters`, `operation.Responses`, `document.Components.SecuritySchemes`, and other collection properties may be null and must be checked or initialized:
   ```csharp
   operation.Responses ??= new OpenApiResponses();
   operation.Parameters?.FirstOrDefault(p => p.Name == "api-version");
   document.Components ??= new();
   document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
   ```

4. **`OpenApiSchema.Nullable` removed** — OpenAPI 3.1 uses JSON Schema `type: ["string", "null"]` instead of `nullable: true`. Schema transformers that set `.Nullable = false` can be removed.

5. **Security requirement values require `List<string>`** — Implicit conversion from `string[]` may not work:
   ```csharp
   // Before
   [oAuthScheme] = scopes
   // After
   [oAuthScheme] = scopes.ToList()
   ```

6. **Interface types in some positions** — Some properties now use interfaces (e.g., `IOpenApiSecurityScheme` instead of `OpenApiSecurityScheme`). Check for compilation errors in transformer code.

## Behavioral Changes

### Cookie login redirects disabled for known API endpoints

ASP.NET Core no longer redirects to login pages for requests to known API endpoints (e.g., those returning `ProblemDetails`). Instead, a `401` status code is returned directly.

This is generally the desired behavior for APIs, but may affect apps that relied on the redirect for API calls.

### Exception diagnostics suppressed when TryHandleAsync returns true

**Security consideration:** When `IExceptionHandler.TryHandleAsync` returns `true`, the exception diagnostics middleware no longer emits diagnostic events for that exception. If handled exceptions include security-relevant events (authentication failures, authorization violations, injection attempts), suppressing diagnostics could create blind spots in security monitoring and audit logging. Ensure your exception handler explicitly emits security-relevant telemetry before returning `true`.
