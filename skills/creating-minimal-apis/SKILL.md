---
name: creating-minimal-apis
description: Build ASP.NET Core Minimal API endpoints for HTTP services, including routing, validation, dependency injection, and OpenAPI documentation. Use when creating new REST API endpoints, lightweight HTTP services, or microservice backends in .NET.
---

# Creating Minimal APIs

## When to Use

- Building a new HTTP API or microservice from scratch
- Adding lightweight endpoints to an existing ASP.NET Core app
- Prototyping an API quickly without controllers
- The user asks to create REST endpoints in .NET

## When Not to Use

- The project uses MVC controllers and the user wants to stay with that pattern
- The user needs SignalR, gRPC, or GraphQL (not HTTP REST)
- The project targets .NET 5 or earlier (Minimal APIs require .NET 6+)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| API requirements | Yes | Endpoints, resources, and operations to expose |
| Existing project | No | Path to an existing ASP.NET Core project; created if absent |

## Workflow

### Step 1: Create or verify the project

If no project exists:

```bash
dotnet new web -o src/MyApi
dotnet sln add src/MyApi
```

Verify the project targets .NET 8 or later for the best Minimal API experience.

### Step 2: Define the data model

```csharp
public record Todo(int Id, string Title, bool IsComplete);
```

Use records for DTOs. They provide immutability and value equality by default.

### Step 3: Create endpoints

Structure endpoints in `Program.cs` or use extension methods for organization:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register services
builder.Services.AddSingleton<ITodoService, TodoService>();

var app = builder.Build();

// Map endpoints
var todos = app.MapGroup("/api/todos");
todos.MapGet("/", (ITodoService service) => service.GetAll());
todos.MapGet("/{id:int}", (int id, ITodoService service) =>
    service.GetById(id) is { } todo
        ? Results.Ok(todo)
        : Results.NotFound());
todos.MapPost("/", (Todo todo, ITodoService service) =>
{
    var created = service.Create(todo);
    return Results.Created($"/api/todos/{created.Id}", created);
});
todos.MapPut("/{id:int}", (int id, Todo todo, ITodoService service) =>
    service.Update(id, todo) ? Results.NoContent() : Results.NotFound());
todos.MapDelete("/{id:int}", (int id, ITodoService service) =>
    service.Delete(id) ? Results.NoContent() : Results.NotFound());

app.Run();
```

### Step 4: Add validation

Install the validation package:

```bash
dotnet add package FluentValidation.DependencyInjectionExtensions
```

Create a validator:

```csharp
using FluentValidation;

public class TodoValidator : AbstractValidator<Todo>
{
    public TodoValidator()
    {
        RuleFor(t => t.Title).NotEmpty().MaximumLength(200);
    }
}
```

Apply validation in the endpoint using an endpoint filter:

```csharp
todos.MapPost("/", (Todo todo, ITodoService service) =>
{
    var created = service.Create(todo);
    return Results.Created($"/api/todos/{created.Id}", created);
}).AddEndpointFilter<ValidationFilter<Todo>>();
```

### Step 5: Add OpenAPI documentation

```csharp
builder.Services.AddOpenApi();

// After building the app:
app.MapOpenApi();
```

Run the app and verify the OpenAPI document is available at `/openapi/v1.json`.

### Step 6: Organize for larger APIs

For APIs with many endpoints, extract into extension methods:

```csharp
// TodoEndpoints.cs
public static class TodoEndpoints
{
    public static RouteGroupBuilder MapTodoEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/todos");
        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Create);
        return group;
    }

    private static IResult GetAll(ITodoService service) => Results.Ok(service.GetAll());
    private static IResult GetById(int id, ITodoService service) =>
        service.GetById(id) is { } todo ? Results.Ok(todo) : Results.NotFound();
    private static IResult Create(Todo todo, ITodoService service) =>
        Results.Created($"/api/todos/{service.Create(todo).Id}", todo);
}

// Program.cs
app.MapTodoEndpoints();
```

### Step 7: Run and test

```bash
dotnet run --project src/MyApi
```

Verify with curl or any HTTP client:

```bash
curl http://localhost:5000/api/todos
curl -X POST http://localhost:5000/api/todos -H "Content-Type: application/json" -d '{"id":0,"title":"Buy milk","isComplete":false}'
```

## Validation

- [ ] `dotnet build` compiles without errors
- [ ] `dotnet run` starts the server successfully
- [ ] GET endpoints return expected data
- [ ] POST endpoint creates resources and returns 201
- [ ] Invalid input returns 400 (if validation is added)
- [ ] OpenAPI document is accessible at `/openapi/v1.json`

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Route conflicts between endpoints | Use `MapGroup` with unique prefixes |
| Missing `[FromBody]` / `[FromQuery]` | Minimal APIs infer binding; use attributes only when ambiguous |
| CORS errors from browser clients | Add `builder.Services.AddCors()` and `app.UseCors()` |
| Port conflicts | Set port in `launchSettings.json` or `--urls` flag |
| Forgetting to register services | All dependencies must be registered in `builder.Services` |
