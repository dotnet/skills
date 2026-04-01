# Migrating from Swashbuckle to Microsoft.AspNetCore.OpenApi

## Migrate or Stay?

Migration is not mandatory. Swashbuckle continues to work on .NET 9+ with version 7.x.
Migrate when:

- You are on .NET 9+ and want to reduce third-party dependencies
- You want the first-party package that ships with the default templates going forward
- Your Swashbuckle customizations are minimal (basic JWT setup, simple metadata)

Stay on Swashbuckle when:

- You rely on heavily customized `IDocumentFilter` / `IOperationFilter` logic — the
  migration cost is non-trivial and the behavior parity must be verified
- You use Swashbuckle's built-in `IncludeXmlComments()` for MVC XML docs and have
  not validated the equivalent setup with the built-in package
- You need features the built-in package does not yet support (advanced polymorphism,
  `oneOf`/`anyOf` discriminators, complex `$ref` scenarios)

---

## Package Changes

```xml
<!-- Remove -->
<PackageReference Include="Swashbuckle.AspNetCore" Version="7.*" />

<!-- Add -->
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="9.*" />

<!-- If you still want Swagger UI instead of Scalar -->
<PackageReference Include="Swashbuckle.AspNetCore.SwaggerUI" Version="7.*" />

<!-- Recommended: replace Swagger UI with Scalar -->
<PackageReference Include="Scalar.AspNetCore" Version="2.*" />
```

---

## Registration Changes

### Before (Swashbuckle)

```csharp
// builder.Services
builder.Services.AddEndpointsApiExplorer();  // Required for minimal APIs
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "My API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", /* ... */);
    options.AddSecurityRequirement(/* ... */);
    options.DocumentFilter<MyDocumentFilter>();
    options.OperationFilter<MyOperationFilter>();
});
```

### After (Microsoft.AspNetCore.OpenApi)

```csharp
// builder.Services
// AddEndpointsApiExplorer() is no longer needed — remove it.
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Info = new OpenApiInfo { Title = "My API", Version = "v1" };
        return Task.CompletedTask;
    });
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddDocumentTransformer<MyDocumentTransformer>();     // replaces DocumentFilter
    options.AddOperationTransformer<MyOperationTransformer>();   // replaces OperationFilter
});
```

---

## Middleware Changes

### Before

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();       // Serves at /swagger/v1/swagger.json
    app.UseSwaggerUI();     // Serves UI at /swagger
}
```

### After — with Scalar (recommended)

```csharp
using Scalar.AspNetCore;

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                   // Serves at /openapi/v1.json
    app.MapScalarApiReference();        // Serves UI at /scalar/v1
}
```

### After — keeping Swagger UI

```csharp
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        // CRITICAL: Must point at the new path — not the old Swashbuckle default.
        options.SwaggerEndpoint("/openapi/v1.json", "v1");
        options.RoutePrefix = "swagger";
    });
}
```

---

## Filter-to-Transformer Mapping

| Swashbuckle | Microsoft.AspNetCore.OpenApi | Notes |
|-------------|------------------------------|-------|
| `IDocumentFilter` | `IOpenApiDocumentTransformer` | Same scope: transforms the whole document |
| `IOperationFilter` | `IOpenApiOperationTransformer` | Same scope: transforms individual operations |
| `ISchemaFilter` | `IOpenApiSchemaTransformer` | Same scope: transforms individual schemas |
| `IParameterFilter` | `IOpenApiOperationTransformer` | Access parameters via `operation.Parameters` |
| `options.DocumentFilter<T>()` | `options.AddDocumentTransformer<T>()` | Direct rename |
| `options.OperationFilter<T>()` | `options.AddOperationTransformer<T>()` | Direct rename |
| `options.SchemaFilter<T>()` | `options.AddSchemaTransformer<T>()` | Direct rename |
| `options.IncludeXmlComments(path)` | `GenerateDocumentationFile` in `.csproj` | XML docs for MVC are auto-loaded; see note below |
| `context.MethodInfo` (in `IOperationFilter`) | `context.Description.ActionDescriptor` | Access endpoint metadata from `ActionDescriptor.EndpointMetadata` |
| `context.ApiDescription` (in `IOperationFilter`) | `context.Description` | Same type: `ApiDescription` |

### XML Documentation Note

With Swashbuckle, you explicitly load the XML file via `IncludeXmlComments()`. With the
built-in package, XML comments on MVC controller actions are picked up automatically
when `GenerateDocumentationFile` is set to `true` in the project file and the XML file
is present alongside the assembly. For minimal API endpoints, use `.WithSummary()` and
`.WithDescription()` — XML comments on handler delegates are not picked up automatically.

---

## Behavioral Differences to Verify

After migrating, check these known behavioral differences:

| Area | Swashbuckle behavior | Built-in package behavior |
|------|---------------------|--------------------------|
| Default spec URL | `/swagger/v1/swagger.json` | `/openapi/v1.json` |
| Spec format | OpenAPI 3.0 | OpenAPI 3.0 (same) |
| `$ref` resolution | Inline + `$ref` mix | More aggressive use of `$ref` components |
| Nullable annotations | Configurable | `nullable: true` emitted for nullable reference types when NRT is enabled |
| Enum representation | Integers by default | Integers by default — add schema transformer for string enums (see transformers.md) |
| Anonymous type schemas | Named after generated types | May differ — test complex response shapes |
| Endpoint ordering in spec | Alphabetical by route | By registration order |

---

## Common Post-Migration Failures

**The spec endpoint returns 404**

The old URL (`/swagger/v1/swagger.json`) no longer exists. Update all references to
`/openapi/v1.json`. Check: CI pipeline spec validation, Postman collections, client
generation scripts, integration tests that assert against the spec.

**The spec JSON is returned but the UI is blank**

If using `UseSwaggerUI`, verify `SwaggerEndpoint` is pointing at `/openapi/v1.json`,
not the old path.

**Some endpoints disappeared from the spec**

Remove `AddEndpointsApiExplorer()` — it should be gone, but it does not cause
disappearing endpoints. More likely cause: a transformer throwing an unhandled exception
during document generation. Check application logs for transformer errors.

**Custom `IDocumentFilter` logic no longer applies**

Rewire it as an `IOpenApiDocumentTransformer`. The `Apply` method signature becomes
`TransformAsync` and is async. The `context` object is `OpenApiDocumentTransformerContext`
rather than `DocumentFilterContext` — check which properties you use and map them.

**`[SwaggerIgnore]` or `SwaggerExcludeAttribute` stopped working**

These are Swashbuckle-specific attributes. Replace with `.ExcludeFromDescription()` on
minimal API endpoints, or `[ApiExplorerSettings(IgnoreApi = true)]` on MVC controller
actions. Both work with the built-in package.

**Security scheme shows in spec but the UI padlock icon is missing**

Verify the scheme name in `SecuritySchemes` (e.g., `"Bearer"`) exactly matches the
`Id` in `OpenApiReference`. The comparison is case-sensitive. Also confirm the
security requirement was applied to operations — check the raw JSON for a `security`
array on each operation.
