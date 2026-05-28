---
name: dotnet-webapi
description: >
  Guides creation and modification of ASP.NET Core Web API endpoints with
  correct HTTP semantics, OpenAPI metadata, and error handling.
  USE FOR: adding new API endpoints (controllers or minimal APIs), wiring up
  OpenAPI/Swagger, creating .http test files, setting up global error handling
  middleware.
  DO NOT USE FOR: general C# coding style, EF Core data access or query
  optimization (use optimizing-ef-core-queries), frontend/Blazor work, gRPC
  services, or SignalR hubs.
license: MIT
---

# ASP.NET Core Web API

Produce well-structured ASP.NET Core Web API endpoints with proper HTTP
semantics, OpenAPI documentation, and error handling.

## When to Use

Use this skill when working on ASP.NET Core HTTP APIs, including:

- adding or modifying Web API endpoints implemented with controllers or minimal APIs;
- wiring up OpenAPI/Swagger metadata and endpoint documentation;
- defining request/response DTOs and consistent HTTP status code behavior;
- adding `.http` files or similar request-based API testing artifacts;
- configuring centralized API error handling middleware or exception mapping.

## When Not to Use

Do not use this skill for:

- general C# coding style or non-API refactoring;
- EF Core data modeling or query optimization work; use `optimizing-ef-core-queries`;
- frontend, Razor, or Blazor UI changes;
- gRPC services;
- SignalR hubs or real-time messaging flows.

## Inputs / prerequisites

Before applying this skill, gather the project context needed to match the
existing API style and wiring:

- the ASP.NET Core entry point, typically `Program.cs`;
- any existing controllers, especially classes inheriting `ControllerBase` or
  using `[ApiController]`;
- any existing minimal API registrations such as `app.MapGet`, `app.MapPost`,
  `app.MapPut`, or `app.MapDelete`;
- related DTO, model, validation, and error-handling types already used by the project;
- available build, run, and test commands so changes can be verified.

If the user asks for a new endpoint, inspect the current project structure first
so the implementation follows the established conventions rather than mixing styles.
## Workflow

### Step 1: Determine the API style

Scan the project for existing endpoint patterns before writing any code.

1. Search for classes inheriting `ControllerBase` or decorated with `[ApiController]`.
2. Search `Program.cs` or endpoint files for `app.MapGet`, `app.MapPost`, etc.
3. If the project already uses **controllers**, continue with controllers.
4. If the project already uses **minimal APIs**, continue with minimal APIs.
5. If neither exists (new project), **default to minimal APIs** unless the user
   explicitly requests controllers.

Do not mix styles in the same project.

### Step 2: Define request and response types

Create dedicated types for API input and output. Never expose EF Core entities
directly in request or response bodies.

**Use `sealed record` for all DTOs.** Records enforce immutability, provide
value-based equality, and produce concise code. Seal them to prevent unintended
inheritance and enable JIT devirtualization (CA1852).

**Naming convention:**

| Role | Convention | Example |
|------|-----------|---------|
| Input (create) | `Create{Entity}Request` | `CreateProductRequest` |
| Input (update) | `Update{Entity}Request` | `UpdateProductRequest` |
| Output (single) | `{Entity}Response` | `ProductResponse` |
| Output (list) | `{Entity}ListResponse` | `ProductListResponse` |

**XML doc comments on all DTOs:** Add `<summary>` XML doc comments to every
request and response type exposed in the API. These comments are automatically
included in the generated OpenAPI specification, producing richer documentation
without extra metadata calls.

Reference: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/openapi-comments

**Date and time values — use `DateTimeOffset`:** When a DTO includes a date or
time property, always use `DateTimeOffset` instead of `DateTime`.
`DateTimeOffset` preserves the UTC offset, avoids ambiguous timezone
conversions, and serializes to ISO 8601 with offset information in JSON — which
is what API consumers expect.

Reference: https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset
**JSON serialization options — preserve existing behavior by default:** For
existing APIs, do **not** introduce stricter serialization/deserialization settings
unless the project already uses them or the user explicitly asks for them. Settings
such as case-sensitive property matching and strict number handling can break
existing clients. For **new projects**, or when strict JSON handling is explicitly
requested, configure options like the following to minimize the potential of
processing malicious requests:

```csharp
// Apply these settings only for new projects, when the existing project already
// uses them, or when the user explicitly requests stricter JSON behavior.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    // disallow reading numbers from JSON strings
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    // match properties with exact casing during deserialization
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
    // reject duplicate JSON property names during deserialization
    options.SerializerOptions.AllowDuplicateProperties = false;
    // omit null properties from serialized output
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
```
**Enum properties — serialize as strings by default:** Unless the user
explicitly requests integer serialization, all enum properties should be
serialized as strings. String-serialized enums are human-readable, less fragile
when values are reordered, and produce better OpenAPI documentation. See Step 4
for the `JsonStringEnumConverter` configuration.

**Response DTOs** — use positional sealed records for concise, immutable output:

```csharp
/// <summary>Represents a product returned by the API.</summary>
public sealed record ProductResponse(
    int Id,
    string Name,
    decimal Price,
    Category Category,
    bool IsAvailable,
    DateTimeOffset CreatedAt);
```

**Request DTOs** — use sealed records with `init` properties so data annotations
work naturally:

```csharp
/// <summary>Payload for creating a new product.</summary>
public sealed record CreateProductRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [Range(0.01, 999999.99)]
    public required decimal Price { get; init; }

    public required Category Category { get; init; }
}
```

Follow the same pattern for `Update{Entity}Request` records, adding any
additional properties the update requires (e.g., `IsAvailable`).

**Minimal API validation — register explicitly:** Data-annotation validation
(`[Required]`, `[MaxLength]`, `[Range]`, etc.) is automatic in MVC controllers,
but minimal APIs require explicit opt-in. For **.NET 10+** projects using minimal
APIs, add the validation services in `Program.cs`:

```csharp
builder.Services.AddValidation();
```

This wires up an endpoint filter that validates parameters decorated with data
annotations before the handler executes, returning a `400 Bad Request` with a
validation problem details response on failure.

Reference: https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis?view=aspnetcore-10.0

**Do not** use mutable classes (`{ get; set; }`) for DTOs. Mutable DTOs allow
accidental modification after construction and lose the self-documenting
immutability that records provide.

### Step 3: Implement the endpoints

Whether using controllers or minimal APIs, follow these HTTP conventions
consistently.

**Organizing minimal API endpoints:** For projects using minimal APIs, organize
endpoints by resource using static classes with a static `Map<Resource>` method.
This pattern keeps endpoint definitions grouped by resource type, making the
code more maintainable and easier to navigate as the API grows.

**Pattern structure:**

1. Create one static class per resource (e.g., `ProductEndpoints`, `CategoryEndpoints`).
2. Define a static `Map<Resource>(this WebApplication app)` extension method.
3. Inside the method, call `MapGet`, `MapPost`, `MapPut`, `MapDelete`, etc. for
   that resource's endpoints.
4. In `Program.cs`, call each resource's `Map` method in order.

**Minimal API return types — prefer `TypedResults`:**

Always prefer `TypedResults` over the `Results` factory. `TypedResults` embeds
response type information in the method signature, giving the OpenAPI generator
richer metadata automatically.

When a handler returns **multiple result types** (e.g., `Ok` or `NotFound`),
annotate the lambda with an explicit `Results<T1, T2>` return type. This
lets you use `TypedResults` while still giving the compiler a common type:

```csharp
async Task<Results<Ok<ProductResponse>, NotFound>> (int id, ...) => ...
```

**Do not** use `TypedResults.Ok(x)` and `TypedResults.NotFound()` in a bare
ternary without an explicit return type annotation. `Ok<T>` and `NotFound` are
different types with no common base the compiler can infer, which causes
`CS1593: Delegate 'RequestDelegate' does not take N arguments` because the
compiler falls back to matching `RequestDelegate(HttpContext)`.

**Fallback — `Results` factory:** If a handler has many conditional branches
(7+ result types), you may use the `Results` factory (`Results.Ok()`,
`Results.NotFound()`) which returns `IResult`, sacrificing compile-time OpenAPI
inference for simpler signatures.

**Status codes:**

| Operation | Success | Common errors |
|-----------|---------|---------------|
| GET (single) | `200 OK` | `404 Not Found` |
| GET (list) | `200 OK` | — |
| POST (create) | `201 Created` with `Location` header | `400 Bad Request`, `409 Conflict` |
| PUT (full update) | `200 OK` | `400 Bad Request`, `404 Not Found` |
| PATCH (partial/action) | `200 OK` | `400 Bad Request`, `404 Not Found` |
| DELETE | `204 No Content` | `404 Not Found`, `409 Conflict` |

**POST 201 responses:** Always return a `Location` header pointing to the
newly created resource.

- Controllers: use `CreatedAtAction(nameof(GetById), new { id = ... }, response)`
- Minimal APIs: use `TypedResults.Created($"/api/products/{id}", response)`

**CancellationToken:** Accept `CancellationToken` in every endpoint signature
and forward it through to all async calls (service methods, EF Core queries,
`HttpClient` calls). This allows the server to stop work when a client
disconnects.

```csharp
// Controller example
[HttpGet("{id}")]
public async Task<ActionResult<ProductResponse>> GetById(
    int id, CancellationToken cancellationToken)
{
    var product = await _productService.GetByIdAsync(id, cancellationToken);
    return product is null ? NotFound() : Ok(product);
}

// Minimal API example — TypedResults with explicit return type (recommended)
app.MapGet("/api/products/{id}", async Task<Results<Ok<ProductResponse>, NotFound>> (
    int id, IProductService service, CancellationToken cancellationToken) =>
{
    var product = await service.GetByIdAsync(id, cancellationToken);
    return product is null ? TypedResults.NotFound() : TypedResults.Ok(product);
});
```

### Step 4: Wire up OpenAPI

Every ASP.NET Core Web API should have OpenAPI documentation. Check whether
the project already has OpenAPI configured before adding it.

**For .NET 9+ projects**, use the built-in ASP.NET Core OpenAPI support
(`builder.Services.AddOpenApi()` + `app.MapOpenApi()` in development).
This is all that is needed — no additional packages required.

**Do NOT add any `Swashbuckle.*` NuGet package** (`Swashbuckle.AspNetCore`,
`Swashbuckle.AspNetCore.SwaggerUI`, `Swashbuckle.AspNetCore.SwaggerGen`,
etc.) to .NET 9+ projects. Swashbuckle has known compatibility issues with
.NET 9+ and .NET 10 OpenAPI types. For projects targeting .NET 8 or earlier,
Swashbuckle is acceptable. If the project already has Swashbuckle installed,
keep it unless the user asks to remove it.

Reference: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview

**OpenAPI metadata on endpoints:** Add descriptive metadata so the generated
documentation is useful, not just a list of routes. For minimal APIs, chain
the metadata methods:

```csharp
app.MapGet("/api/products/{id}", handler)
    .WithName("GetProductById")
    .WithSummary("Get a product by ID")
    .WithDescription("Returns the full product details including category.")
    .Produces<ProductResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);
```

**Enum serialization (strings by default):** Configure JSON serialization so
enums appear as readable strings in both API responses and OpenAPI schemas.
Always add this configuration unless the user explicitly requests integer
enum serialization. Configure it for both minimal APIs and controllers, as
they use different option types:

```csharp
// Minimal APIs
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Controllers / MVC
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
```

### Step 5: Set up error handling

Use a global exception handler so that individual endpoints do not need
try-catch blocks. Return RFC 7807 Problem Details for all error responses.

**For .NET 8+ projects**, prefer the built-in exception handler middleware:

```csharp
builder.Services.AddProblemDetails();

app.UseExceptionHandler();
app.UseStatusCodePages();
```

If the project needs custom exception-to-status-code mapping (e.g., a
`NotFoundException` should return 404), implement `IExceptionHandler`:

```csharp
internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Bad Request"),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Conflict"),
            _ => (0, (string?)null)
        };

        if (statusCode == 0)
            return false; // Let the default handler deal with it

        // Important: returning true below suppresses the exception diagnostics middleware
        // for this exception, so ensure it is logged/telemetrized before returning.
        logger.LogWarning(exception, "Handled API exception: {Title}", title);

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            // Do not use exception.Message here — it may leak sensitive internal details.
            // Use a safe, user-facing message instead.
            Detail = title,
            Instance = httpContext.Request.Path
        }, cancellationToken);

        return true;
    }
}
```

Register it:

```csharp
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

app.UseExceptionHandler();
```

**File placement:** Always place exception handler classes in a `Middleware/`
folder to maintain consistent project organization. Do not place them at the
project root.

### Step 6: Use a service layer

Do not inject data stores directly into controllers or endpoint handlers.
Create a service interface and a sealed implementation class that owns the
data access logic and mapping between entities and request/response types.

Always define an interface for every service — this enables unit testing with
mocks and follows the Dependency Inversion Principle:

```csharp
// Services/IProductService.cs
public interface IProductService
{
    Task<IReadOnlyList<ProductResponse>> GetAllAsync(CancellationToken ct);
    Task<ProductResponse?> GetByIdAsync(int id, CancellationToken ct);
    Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken ct);
}

// Services/ProductService.cs
public sealed class ProductService(...) : IProductService
{
    // Data access logic, entity-to-DTO mapping
}
```

Register with the interface, not the concrete type:

```csharp
// In Program.cs
builder.Services.AddScoped<IProductService, ProductService>();
```

For EF Core data access patterns (migrations, Fluent API configuration,
`AsNoTracking`, seed data), see the `optimizing-ef-core-queries` skill.

### Step 7: Create a .http test file

After implementing endpoints, create a `.http` file in the project root that
demonstrates how to call every new endpoint. This serves as living
documentation and a quick manual test harness.

```http
@baseUrl = http://localhost:5000

### Get all products
GET {{baseUrl}}/api/products

### Get product by ID
GET {{baseUrl}}/api/products/1

### Create a product
POST {{baseUrl}}/api/products
Content-Type: application/json

{
  "name": "Wireless Mouse",
  "price": 29.99,
  "category": "Electronics"
}

### Delete a product
DELETE {{baseUrl}}/api/products/1
```

Include at least one request per endpoint with realistic bodies. Show error
paths (e.g., non-existent IDs). Match the port to `launchSettings.json`.

### Step 8: Build and verify

1. Run `dotnet build` — confirm zero errors and zero warnings.
2. Start the app and verify the OpenAPI document loads (default: `/openapi/v1.json`).
3. Run the requests in the `.http` file and confirm correct status codes.

## Validation

- [ ] All endpoints return correct HTTP status codes per the table in Step 3
- [ ] POST endpoints return `201 Created` with a `Location` header
- [ ] DELETE endpoints return `204 No Content`
- [ ] Every endpoint signature includes `CancellationToken`
- [ ] `CancellationToken` is forwarded to all downstream async calls
- [ ] OpenAPI document is generated and includes all new endpoints
- [ ] Endpoints have summary/description metadata for OpenAPI
- [ ] Enum values appear as strings in JSON responses and OpenAPI schemas (unless user explicitly requested integer serialization)
- [ ] Error responses use RFC 7807 Problem Details format
- [ ] Domain entities are not exposed directly in API request/response bodies
- [ ] All API-exposed DTOs have `<summary>` XML doc comments
- [ ] Date and time properties use `DateTimeOffset`, not `DateTime`
- [ ] A `.http` file exists with a request for every new endpoint
- [ ] `dotnet build` passes with zero errors and zero warnings
- [ ] All DTOs are `sealed record` types (not mutable classes)
- [ ] Minimal API handlers use `TypedResults` with explicit `Results<T1, T2>` return types
- [ ] Every service has a corresponding interface registered in DI
- [ ] Exception handlers are placed in the `Middleware/` folder

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Exposing domain entities as API responses | Create separate `sealed record` request/response types. Entities leak navigation properties and internal fields. |
| Forgetting `CancellationToken` | Add to every endpoint and forward through the entire async call chain. |
| Returning `200 OK` from POST create | Return `201 Created` with a `Location` header. |
| Missing OpenAPI metadata | Chain `.WithName()`, `.WithSummary()`, `.WithDescription()`, `.Produces<T>()` on every endpoint. |
| Injecting data stores directly into endpoints | Use a service layer with an interface for separation and testability. |
| Mixing controller and minimal API styles | Pick one per project and be consistent. |
| `TypedResults` in ternary without explicit return type | `Ok<T>` and `NotFound` have no common base — annotate with `Task<Results<Ok<T>, NotFound>>` or fall back to `Results` factory. |
| Using mutable classes for DTOs | Use `sealed record` with positional syntax (responses) or `init` properties (requests). |
| Registering services without interfaces | Define `IService` and register with `AddScoped<IService, Service>()`. |
| Adding any `Swashbuckle.*` package to new .NET 9+ projects | Use built-in `AddOpenApi()` + `MapOpenApi()`. Do not add `Swashbuckle.AspNetCore`, `Swashbuckle.AspNetCore.SwaggerUI`, or any other Swashbuckle package. |
| Missing XML doc comments on DTOs | Add `<summary>` XML doc comments to every request and response type. These flow into the generated OpenAPI spec automatically. |
| Using `DateTime` for date/time properties | Use `DateTimeOffset` instead — it preserves UTC offset, avoids timezone ambiguity, and serializes correctly in JSON. |
| Serializing enums as integers | Configure `JsonStringEnumConverter` so enums serialize as strings by default. Only use integer serialization if the user explicitly requests it. |

## More Info

- [ASP.NET Core Web API overview](https://learn.microsoft.com/en-us/aspnet/core/web-api/) — fundamental concepts for building Web APIs
- [OpenAPI in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview) — built-in OpenAPI support in .NET 9+
- [OpenAPI from XML comments](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/openapi-comments) — how XML doc comments flow into the OpenAPI spec
- [Minimal APIs overview](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/overview) — routing, parameter binding, and response types
- [Handle errors in ASP.NET Core APIs](https://learn.microsoft.com/en-us/aspnet/core/web-api/handle-errors) — Problem Details and exception handling
- [DateTimeOffset](https://learn.microsoft.com/en-us/dotnet/api/system.datetimeoffset) — preferred type for date/time values in APIs
