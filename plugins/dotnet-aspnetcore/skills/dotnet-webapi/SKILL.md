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

Produce ASP.NET Core Web API endpoints with correct HTTP semantics, OpenAPI
documentation, and centralized error handling. Applies to controllers or
minimal APIs, DTO design, and `.http` test files. Not for general C# style,
EF Core query work (use `optimizing-ef-core-queries`), Blazor, gRPC, or SignalR.

## Match the existing style

Inspect the project first (`Program.cs`, existing `ControllerBase`/`[ApiController]`
classes, existing `app.MapGet/MapPost` registrations). Continue with whatever the
project already uses; for a brand-new project default to minimal APIs. Never mix
controllers and minimal APIs in one project.

## Requirements

**DTOs**
- Use `sealed record` types (positional for responses, `init` properties for
  requests) — never mutable classes and never expose domain/EF entities directly.
- Name them `Create{Entity}Request`, `Update{Entity}Request`, `{Entity}Response`.
- Add a `<summary>` XML doc comment to every request/response type; these flow
  into the OpenAPI spec.
- Use `DateTimeOffset` (not `DateTime`) for date/time properties.
- Serialize enums as strings via `JsonStringEnumConverter` unless the user asks
  for integers. Register for minimal APIs and controllers:
  ```csharp
  builder.Services.ConfigureHttpJsonOptions(o =>
      o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
  builder.Services.AddControllers()
      .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
  ```

**Endpoints**
- Accept `CancellationToken` in every endpoint signature and forward it to all
  async calls.
- Status codes: GET `200`/`404`; POST create `201 Created` with a `Location`
  header (`TypedResults.Created(...)` or controller `CreatedAtAction(...)`);
  PUT `200`; DELETE `204`.
- Minimal APIs: use `TypedResults`. When a handler returns more than one result,
  annotate the lambda with an explicit `Results<T1, T2>` return type (do not use
  `TypedResults.Ok`/`NotFound` in a bare ternary — they have no common base):
  ```csharp
  app.MapGet("/api/products/{id}",
      async Task<Results<Ok<ProductResponse>, NotFound>> (
          int id, IProductService service, CancellationToken ct) =>
      {
          var product = await service.GetByIdAsync(id, ct);
          return product is null ? TypedResults.NotFound() : TypedResults.Ok(product);
      })
      .WithName("GetProductById")
      .WithSummary("Get a product by ID")
      .WithDescription("Returns the full product details.");
  ```

**OpenAPI**
- For .NET 9+, use built-in `builder.Services.AddOpenApi()` + `app.MapOpenApi()`.
- Do NOT add any `Swashbuckle.*` package to .NET 9+ projects (Swashbuckle is fine
  for .NET 8 and earlier; keep it if already present).
- Give every endpoint `.WithName()`, `.WithSummary()`, and `.WithDescription()`.

**Error handling**
- Use a global exception handler; endpoints must not wrap logic in try/catch.
- Return RFC 7807 `ProblemDetails` for all errors. Register
  `AddProblemDetails()` and call `app.UseExceptionHandler()`.
- For custom exception-to-status mapping, implement a `sealed`
  `IExceptionHandler` placed in a `Middleware/` folder; map
  `KeyNotFoundException`→404, `ArgumentException`→400,
  `InvalidOperationException`→409, and never expose raw exception messages:
  ```csharp
  internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger)
      : IExceptionHandler
  {
      public async ValueTask<bool> TryHandleAsync(
          HttpContext ctx, Exception ex, CancellationToken ct)
      {
          var (status, title) = ex switch
          {
              KeyNotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
              ArgumentException => (StatusCodes.Status400BadRequest, "Bad Request"),
              InvalidOperationException => (StatusCodes.Status409Conflict, "Conflict"),
              _ => (0, (string?)null)
          };
          if (status == 0) return false;
          logger.LogWarning(ex, "Handled API exception: {Title}", title);
          ctx.Response.StatusCode = status;
          await ctx.Response.WriteAsJsonAsync(new ProblemDetails
          {
              Status = status, Title = title, Detail = title,
              Instance = ctx.Request.Path
          }, ct);
          return true;
      }
  }
  // builder.Services.AddExceptionHandler<ApiExceptionHandler>();
  // builder.Services.AddProblemDetails();
  // app.UseExceptionHandler();
  ```

**Service layer**
- Do not inject data stores into endpoints. Define an interface per service and
  register it by interface: `builder.Services.AddScoped<IProductService, ProductService>();`.
  Implementations are `sealed`. For EF Core specifics see `optimizing-ef-core-queries`.

**Verification**
- Create a `.http` file in the project root with one request per endpoint
  (including an error path), matching the port in `launchSettings.json`.
- `dotnet build` must pass with zero errors and zero warnings; confirm the
  OpenAPI document loads (default `/openapi/v1.json`).

## More Info

- [ASP.NET Core Web API](https://learn.microsoft.com/en-us/aspnet/core/web-api/)
- [OpenAPI in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview)
- [Handle errors in ASP.NET Core APIs](https://learn.microsoft.com/en-us/aspnet/core/web-api/handle-errors)