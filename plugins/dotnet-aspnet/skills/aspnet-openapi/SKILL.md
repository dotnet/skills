---
name: aspnet-openapi
description: >
  Add OpenAPI documentation to ASP.NET Core APIs (.NET 8+). Covers technology selection
  (Microsoft.AspNetCore.OpenApi vs Swashbuckle vs NSwag), Scalar and Swagger UI setup,
  JWT Bearer security scheme configuration, endpoint metadata for minimal APIs and MVC
  controllers, and document/operation transformers.
  USE FOR: adding an OpenAPI spec endpoint to a new or existing ASP.NET Core project,
  setting up an interactive API explorer (Scalar, Swagger UI), configuring JWT Bearer
  security in the OpenAPI document, annotating minimal API endpoints with response types
  and tags, customizing the generated spec with transformers, or migrating from
  Swashbuckle.AspNetCore to the built-in package on .NET 9+.
  DO NOT USE FOR: generating typed API client code from an OpenAPI spec (use NSwag CLI
  or the dotnet-openapi tooling instead), adding OpenAPI to non-ASP.NET projects, or
  validating incoming requests against a schema at runtime.
---

# ASP.NET Core OpenAPI

.NET 9 changed the landscape. `Microsoft.AspNetCore.OpenApi` is now the first-party
solution, backed by the ASP.NET Core team, and ships in the default web project templates.
Swashbuckle — the de-facto standard for the previous decade — still works but is no longer
the default, and its major versions have lagged .NET releases by months. NSwag remains the
right choice when client code generation is the primary goal.

## Stop Signals

- **Need to generate C# or TypeScript API clients?** — Use NSwag or `dotnet-openapi`. This skill covers spec *generation*, not *consumption*.
- **Targeting .NET 8 or earlier?** — `AddOpenApi()` / `MapOpenApi()` document generation requires .NET 9+. Use Swashbuckle 6.x on .NET 8.
- **Already on Swashbuckle and no .NET 9 migration planned?** — Don't migrate just for its own sake. See [references/swashbuckle-migration.md](references/swashbuckle-migration.md) if migration is wanted later.
- **Review request on existing OpenAPI config?** — Jump directly to the Validation checklist.

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Target framework | Yes | Determines which packages are available |
| API style | Yes | Minimal APIs or MVC controllers — affects annotation syntax |
| Auth scheme | Recommended | JWT Bearer, OAuth2, or API key — drives security scheme config |
| Existing packages | Recommended | Check whether Swashbuckle or NSwag is already referenced |

## Workflow

### Step 1: Choose the Package

| Situation | Package | Reason |
|-----------|---------|--------|
| New project on .NET 9+ | `Microsoft.AspNetCore.OpenApi` | First-party, default in templates, actively maintained by the ASP.NET Core team |
| New project on .NET 8 | `Swashbuckle.AspNetCore` 6.x | `AddOpenApi()` / `MapOpenApi()` require .NET 9+ |
| Need typed client code generation | `NSwag.AspNetCore` | Only mature option with C#/TypeScript codegen; pair with the spec output |
| Complex polymorphism or discriminators | `NSwag.AspNetCore` | More schema control than either alternative |
| Existing project already on Swashbuckle | Keep Swashbuckle | Migration is optional — see [references/swashbuckle-migration.md](references/swashbuckle-migration.md) |

**Hard rule:** Do not mix the built-in generator (`Microsoft.AspNetCore.OpenApi`) with the
Swashbuckle *document generator* (`Swashbuckle.AspNetCore` via `AddSwaggerGen`/`UseSwagger`)
in the same project. Both enumerate endpoints at startup and produce conflicting OpenAPI
documents — pick one generator. Using the standalone `Swashbuckle.AspNetCore.SwaggerUI`
package alongside `Microsoft.AspNetCore.OpenApi` is fine, as long as it only points at the
built-in `/openapi/v1.json` endpoint (shown in Step 4).

---

### Step 2: Set Up Microsoft.AspNetCore.OpenApi (.NET 9+)

```bash
dotnet add package Microsoft.AspNetCore.OpenApi
```

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();              // Register document generation

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                       // Serves spec JSON at /openapi/v1.json
}

app.Run();
```

> **CRITICAL:** `MapOpenApi()` serves raw JSON — it is **not** a UI. Add a UI package in Step 4.

> **CRITICAL:** The default spec path is `/openapi/v1.json`. This is **not** the Swashbuckle
> default of `/swagger/v1/swagger.json`. Any tooling, CI pipeline, or Postman collection
> importing from the old path will silently get a 404.

**Multiple document versions:**

```csharp
builder.Services.AddOpenApi("v1");
builder.Services.AddOpenApi("v2");

// Both served at their named paths: /openapi/v1.json and /openapi/v2.json
app.MapOpenApi("/openapi/{documentName}.json");
```

---

### Step 3: Set Up Swashbuckle (.NET 8 / existing projects)

```bash
# 6.x for .NET 8 — 7.x for .NET 9
dotnet add package Swashbuckle.AspNetCore
```

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// CRITICAL: Required for minimal API endpoints to appear in the spec.
// Swashbuckle uses the ApiExplorer infrastructure, which minimal APIs do NOT
// register automatically. Without this line every MapGet/MapPost endpoint is
// invisible in the generated spec. MVC controllers are unaffected.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();       // Serves spec at /swagger/v1/swagger.json
    app.UseSwaggerUI();     // Serves Swagger UI at /swagger
}

app.Run();
```

---

### Step 4: Add an API Explorer UI

#### Scalar (recommended with Microsoft.AspNetCore.OpenApi)

```bash
dotnet add package Scalar.AspNetCore
```

```csharp
using Scalar.AspNetCore;

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();    // Default at /scalar/v1
}
```

Scalar reads from `MapOpenApi()` automatically. For customization:

```csharp
app.MapScalarApiReference(options =>
{
    options.WithTitle("Contoso API");
    options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    options.WithPreferredScheme("Bearer");
});
```

#### Swagger UI alongside Microsoft.AspNetCore.OpenApi

If Swagger UI is a hard requirement, use only the UI package — not the full Swashbuckle stack:

```bash
dotnet add package Swashbuckle.AspNetCore.SwaggerUI
```

```csharp
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        // CRITICAL: Point at the built-in package's path, not the Swashbuckle default.
        options.SwaggerEndpoint("/openapi/v1.json", "v1");
        options.RoutePrefix = "swagger";
    });
}
```

---

### Step 5: Annotate Endpoints

The spec is only as useful as the metadata you put in. Sparse annotations produce a sparse,
useless spec — and waste the entire investment in Step 2–4.

**Minimal APIs:**

```csharp
app.MapGet("/products/{id:int}", async (int id, IProductRepository repo) =>
{
    var product = await repo.GetByIdAsync(id);
    return product is null ? Results.NotFound() : Results.Ok(product);
})
.WithName("GetProductById")
.WithSummary("Get a product by ID")
.WithDescription("Returns a single product including pricing and stock level.")
.WithTags("Products")
.Produces<Product>(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost("/products", async ([FromBody] CreateProductRequest req, IProductRepository repo) =>
{
    var id = await repo.CreateAsync(req);
    return Results.CreatedAtRoute("GetProductById", new { id });
})
.WithName("CreateProduct")
.WithTags("Products")
.Accepts<CreateProductRequest>("application/json")
.Produces<Product>(StatusCodes.Status201Created)
.ProducesValidationProblem()
.ProducesProblem(StatusCodes.Status409Conflict);

// Exclude internal/infra endpoints entirely
app.MapGet("/internal/health", () => "ok")
   .ExcludeFromDescription();
```

**MVC Controllers:** Use `[ProducesResponseType]`, `[Produces]`, `[Consumes]`, and XML
`<summary>` / `<remarks>` doc comments. Both packages consume these through the ApiExplorer
infrastructure. Enable XML doc generation:

```xml
<!-- In your .csproj -->
<PropertyGroup>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
</PropertyGroup>
```

For Swashbuckle, wire up the XML file in `AddSwaggerGen`:

```csharp
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFile));
});
```

---

### Step 6: Configure JWT Bearer Security Scheme

The built-in package does not auto-detect `AddAuthentication()`. You must declare the
security scheme via a document transformer.

```csharp
// Program.cs
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
});
```

```csharp
// BearerSecuritySchemeTransformer.cs
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            In = ParameterLocation.Header,
            BearerFormat = "JWT",
            Description = "Enter a valid JWT. Example: eyJhbGci..."
        };

        // Apply to every operation. For per-endpoint selective auth,
        // use an operation transformer — see references/transformers.md.
        var securityRequirement = new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            }] = []
        };

        foreach (var pathItem in document.Paths.Values)
        {
            foreach (var operation in pathItem.Operations.Values)
            {
                operation.Security ??= [];
                operation.Security.Add(securityRequirement);
            }
        }

        return Task.CompletedTask;
    }
}
```

> For **per-endpoint** security (some routes public, some require auth), or for **OAuth2**
> and **API key** schemes, see [references/transformers.md](references/transformers.md).

For **Swashbuckle**, the equivalent uses `AddSecurityDefinition` and `AddSecurityRequirement`
inside `AddSwaggerGen` — full examples in [references/transformers.md](references/transformers.md).

---

### Step 7: Secure the Spec Endpoint

The OpenAPI document lists every endpoint, parameter, and auth scheme. Do not expose it
publicly in production.

**Option A — Development only** (most common for internal or backend APIs):

```csharp
// Already shown — wrap MapOpenApi() and the UI in IsDevelopment().
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
```

**Option B — Authenticated users only** (staging, internal portals):

```csharp
app.MapOpenApi()
   .RequireAuthorization();
```

**Option C — Specific policy or role**:

```csharp
app.MapOpenApi()
   .RequireAuthorization("InternalToolsPolicy");
```

> **CRITICAL:** Do not combine Option B/C with the JWT transformer from Step 6 without
> verifying the flow end-to-end. If the spec endpoint requires a token, and the UI reads the
> spec before the user authenticates, the UI will load blank. Either serve the UI and spec
> without auth (Options A), or ensure both are behind the same authenticated session.

---

## Validation

- [ ] `dotnet build -warnaserror` completes cleanly
- [ ] Navigate to the spec path in a running instance — verify well-formed JSON is returned
- [ ] Every endpoint with a non-trivial return type has at least one `Produces<T>()` or `[ProducesResponseType]` annotation
- [ ] Error paths return `ProblemDetails` — not anonymous `{ error: "..." }` objects
- [ ] If auth is configured, the `securitySchemes` section appears in the spec JSON and the scheme name matches the `$ref` value exactly (case-sensitive)
- [ ] The spec endpoint is either restricted to Development or requires authorization — never `AllowAnonymous` in production

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Forgot `AddEndpointsApiExplorer()` with Swashbuckle | Minimal API endpoints won't appear — MVC controllers are unaffected. This is the single most common Swashbuckle setup bug |
| Spec path mismatch after switching packages | Built-in defaults to `/openapi/v1.json`; Swashbuckle defaults to `/swagger/v1/swagger.json`. Update all downstream tooling explicitly |
| Mixed `Microsoft.AspNetCore.OpenApi` + Swashbuckle document generator | Both enumerate endpoints at startup and produce conflicting specs. Remove one generator. The standalone `Swashbuckle.AspNetCore.SwaggerUI` package is fine alongside the built-in package |
| Security scheme `$ref` name mismatch | The `Id` in `OpenApiReference` must exactly match the key in `SecuritySchemes` — `"Bearer"` ≠ `"bearer"` |
| Spec endpoint returns 401 unexpectedly | Either wrapped in `IsDevelopment()` (run in Development env) or secured with `RequireAuthorization()` (authenticate first, or remove the auth requirement from the spec endpoint) |
| Swashbuckle 6.x on .NET 9 | Swashbuckle 6.x does not support .NET 9+ reflection APIs. Upgrade to 7.x or migrate to the built-in package |
| `MapOpenApi()` produces empty `paths` | `AddOpenApi()` was not called, `MapOpenApi()` was mapped on a different `app` instance or route than your API endpoints, or you're requesting a document name that doesn't match the one registered in `AddOpenApi()` |
| Scalar UI loads but shows no auth button | `WithPreferredScheme("Bearer")` not set, or the security scheme was not added to `document.Components.SecuritySchemes` |

## Reference Files

- **[references/transformers.md](references/transformers.md)** — Document, operation, and schema transformers for `Microsoft.AspNetCore.OpenApi`; equivalent `IDocumentFilter`, `IOperationFilter`, and `ISchemaFilter` patterns for Swashbuckle; OAuth2 and API key scheme setup. **Load when** customizing the generated spec, configuring non-JWT security schemes, adding XML documentation to the spec, or applying per-endpoint auth annotations.

- **[references/swashbuckle-migration.md](references/swashbuckle-migration.md)** — Step-by-step migration from `Swashbuckle.AspNetCore` to `Microsoft.AspNetCore.OpenApi` on .NET 9+, including package changes, middleware rewrites, filter-to-transformer mapping, and common post-migration failures. **Load when** the project currently uses Swashbuckle and the developer wants to move to the built-in package.
