---
name: scaffold-generate-aspnet
description: Generate or scaffold ASP.NET Core code — Razor Pages, Blazor components, MVC controllers, views, and Minimal API endpoints — without using ASP.NET Core CLI scaffolding/code-generation tools. Use when (1) adding CRUD pages, views, or API endpoints backed by Entity Framework (EF Core) and a database, (2) generating code to create, read, update, and delete data using a DbContext, (3) scaffolding UI components that match the project's existing CSS framework and coding patterns, or (4) creating data-driven forms, tables, and navigation for a model class. Do not use for non-ASP.NET projects or when CLI-based scaffolding is preferred.
---

# Generate or Scaffold ASP.NET Core Code

Generate ASP.NET Core scaffolded code — controllers, views, Razor Pages, Blazor components, Minimal API endpoints. The generated code matches the project's existing CSS framework, layout conventions, and coding patterns. No CLI-based scaffolding/code-generation tools are used; standard `dotnet` CLI commands for build, restore, and migrations are still expected.

## When to Use

- Adding CRUD pages, views, or components for a model in an ASP.NET Core project
- Scaffolding API controllers or Minimal API endpoints with Entity Framework Core
- Generating Razor Pages, MVC views, or Blazor components backed by a DbContext

## When Not to Use

- The project is not an ASP.NET Core project
- You need to scaffold non-web artifacts (class libraries, console apps, etc.)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Scaffolding request | Yes | Natural-language description of what to scaffold (see format below) |
| Project file path | Yes | Full path to the target `.csproj` file |
| Solution root path | Recommended | Full path to the solution root for multi-project solutions |

### Scaffolding Request Format

The scaffolding request should be a natural-language description of what to scaffold. The request must include the target project path and enough detail for the agent to generate the correct code. Examples:

**Razor Pages with EF:**
```
Scaffold Razor Pages with CRUD for the `<ModelName>` model (from `<Namespace>`) in project `<path-to-csproj>`.
Create a new DbContext `<DbContextName>` using <database-provider>.
Also scaffold CRUD for any entity that `<ModelName>` depends on via required foreign keys, so parent entities can be created first.
```

**Blazor CRUD components:**
```
Scaffold Blazor CRUD components for the `<ModelName>` model (from `<Namespace>`) in project `<path-to-csproj>`.
Create a new DbContext `<DbContextName>` using <database-provider>.
```

**Minimal API endpoints:**
```
Scaffold Minimal API endpoints for the `<ModelName>` model (from `<Namespace>`) in project `<path-to-csproj>`.
Name the endpoints class `<EndpointsClassName>`.
Create a new DbContext `<DbContextName>` using <database-provider>.
Enable OpenAPI support.
```

**MVC Controller with views:**
```
Scaffold an MVC controller with views and Entity Framework for the `<ModelName>` model (from `<Namespace>`) in project `<path-to-csproj>`.
Name the controller `<ControllerName>`.
Use existing DbContext `<DbContextName>`.
Generate views.
```

**Empty items (no EF):**
```
Scaffold an empty Razor Page named `<PageName>` in project `<path-to-csproj>`.
```

## Workflow

### Step 1: Understand the Scaffolding Request

Parse the scaffolding request to identify:
- **Scaffolder type**: Razor Pages, Blazor components, MVC controller, Minimal API, empty page/view/component
- **Model class** and its namespace
- **DbContext**: new or existing, database provider (SQLite, SQL Server, etc.)
- **Named items**: controller name, endpoints class name, page name, view name, area name
- **Options**: OpenAPI, async actions, partial view, custom layout
- **FK scope**: whether to also scaffold CRUD for parent entities referenced by required foreign keys

### Step 2: Discover UI Style (non-API scaffolders only)

Skip this step for API controllers and Minimal API endpoints.

1. Inspect the project's layout file (`_Layout.cshtml`, `MainLayout.razor`, or equivalent)
2. Inspect the main CSS file (`site.css`, `app.css`, Tailwind config, etc.)
3. Inspect 1–2 existing pages, views, or components in the project

All generated files MUST match the existing UI framework, CSS classes, and conventions. If Bootstrap is used, generate Bootstrap markup. If Tailwind is used, generate Tailwind markup.

### Step 3: Apply Blazor-Specific Rules (Blazor scaffolders only)

Skip this step for non-Blazor scaffolders.

- `[SupplyParameterFromForm]` properties MUST use `= new()` (not `null!`) — prevents `EditForm` crash on initial GET
- `Program.cs` must chain `.AddInteractiveServerComponents()` on `AddRazorComponents()` and `.AddInteractiveServerRenderMode()` on `MapRazorComponents<App>()`
- Do not replace existing chained render mode calls (e.g., `.AddInteractiveWebAssemblyRenderMode()`)

### Step 4: Generate Code

Generate all code files manually. Follow these constraints:

- **DO NOT** use any scaffolding CLI tools
- **DO NOT** add packages beyond those required for the scaffolded functionality (e.g., do not add `RuntimeCompilation`, or other convenience packages)
- Match the coding style of existing files in the project (naming conventions, indentation, namespace patterns)

#### Enrich API Endpoints with OpenAPI Metadata (API scaffolders only)

When generating Minimal API or MVC API endpoints with OpenAPI support enabled, add rich metadata to every endpoint so the OpenAPI document is descriptive and useful:

- `.WithName("GetTodoItems")` — unique operation ID for each endpoint
- `.WithTags("TodoItems")` — group endpoints by resource
- `.WithDescription("Returns all todo items")` — human-readable summary
- `.Produces<List<TodoItem>>(StatusCodes.Status200OK)` — document success response type
- `.Produces(StatusCodes.Status404NotFound)` — document error responses
- `.ProducesValidationProblem()` — for endpoints that validate input
- `.WithOpenApi()` — opt the endpoint into OpenAPI generation (if not already globally enabled)

Example for a Minimal API GET endpoint:
```csharp
group.MapGet("/", async (TodoDbContext db) =>
        await db.TodoItems.ToListAsync())
    .WithName("GetAllTodoItems")
    .WithTags("TodoItems")
    .WithDescription("Returns all todo items")
    .Produces<List<TodoItem>>(StatusCodes.Status200OK);
```

### Step 5: Set Up Entity Framework (if applicable)

Skip this step if the scaffolding request does not involve Entity Framework.

- **DO NOT** seed the database in `Program.cs` — always use migrations
- For `dotnet ef`: prefer `dotnet tool restore` from a local tool manifest. Only install globally if no manifest exists
- Inspect the model for navigation properties and foreign keys. Ensure CRUD endpoints/pages exist for referenced entities
- For API scaffolders: the `.http` file MUST create parent entities before child entities. Use FK values consistent with creation order

### Step 6: Generate .http File (API scaffolders only)

Skip this step for non-API scaffolders.

1. Create a `.http` file named `{ModelName}.http` in the project directory. If a file with that name exists, append a numeric suffix (`Product2.http`, `Product3.http`) until unique
2. Include sample requests for every CRUD endpoint scaffolded, including endpoints for parent/dependent entities
3. Every request must target the correct URL path matching an actual mapped endpoint
4. Request labels must accurately describe the action (e.g., "Create a category" must POST to the categories endpoint)
5. Order requests by dependency: create parent entities before child entities
6. POST payloads for child entities MUST use foreign key values matching IDs from parent entity creation requests
7. Do NOT use placeholder or assumed FK values

### Step 7: Verify

1. Run `dotnet restore && dotnet build` from the project directory
2. If Entity Framework is used:
   - Run `dotnet ef migrations add InitialCreate`
   - Run `dotnet ef database update`
3. If API scaffolder:
   - Inspect `Properties/launchSettings.json` — if a profile named `https` exists, use `dotnet run --launch-profile https`
   - Execute EVERY `.http` request one at a time in dependency order
   - Report method, URL, and status code for each request
   - Stop and fix if any request returns non-2xx
4. Fix all errors until a clean build succeeds

## Validation

- [ ] All generated files match the project's existing CSS framework and conventions
- [ ] `dotnet build` succeeds with zero errors
- [ ] EF migrations apply cleanly (if applicable)
- [ ] All `.http` requests return 2xx status codes (if API scaffolder)
- [ ] No forbidden packages were added (`RuntimeCompilation`, etc.)
- [ ] No CLI scaffolding tools were invoked
- [ ] Blazor `[SupplyParameterFromForm]` properties use `= new()` (if Blazor scaffolder)

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Generated UI doesn't match project's CSS framework | Always inspect layout and CSS files before generating code (Step 2) |
| Blazor `EditForm` crashes on initial GET | Use `= new()` not `null!` for `[SupplyParameterFromForm]` properties |
| EF migration fails due to missing parent entity CRUD | Scaffold CRUD for FK-dependent entities when request mentions foreign keys |
| `.http` file has wrong FK values | Order requests by dependency; use values consistent with creation order |
| `dotnet ef` not found | Try `dotnet tool restore` first; only install globally as fallback |
| Added unnecessary packages | Only add packages explicitly required for the scaffolded functionality |
| Generated code uses different naming conventions | Inspect existing project files to match naming patterns before generating |

## References

- [Entity Framework Core DbContext Lifetime, Configuration, and Initialization](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/)
- [OpenAPI overview in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview?view=aspnetcore-10.0) — .NET 10 specific; similar pages exist for other versions
