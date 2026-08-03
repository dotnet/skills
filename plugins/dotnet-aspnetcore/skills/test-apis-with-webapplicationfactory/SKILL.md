---
name: test-apis-with-webapplicationfactory
description: >
  Write end-to-end integration tests for an ASP.NET Core API with WebApplicationFactory.
  USE FOR: adding integration tests that drive endpoints over HTTP through an in-memory test host;
  setting up WebApplicationFactory<Program> and an HttpClient; making a minimal API's Program reachable
  from a test project; replacing the app's DbContext with an isolated test database; seeding per-test
  data; asserting HTTP status codes and JSON payloads.
  DO NOT USE FOR: pure unit tests of a service or handler in isolation (no HTTP host needed); authoring
  the endpoints themselves (use author-minimal-api-endpoints or author-controller-endpoints); load or
  performance testing; service-layer structure (use structure-api-business-logic).
license: MIT
---

# Test APIs with WebApplicationFactory

Drive the real HTTP pipeline in memory, against an isolated database, and assert both the status code and the payload.

## Host the app and get a client

Add `Microsoft.AspNetCore.Mvc.Testing` to the test project (`dotnet add package Microsoft.AspNetCore.Mvc.Testing`) and a reference to the API project. A minimal API built from top-level statements has an inaccessible generated `Program`, so expose it: add `public partial class Program;` at the end of `Program.cs` (or use `[assembly: InternalsVisibleTo("YourTests")]` in the API project).

Give each test its own factory rather than sharing one across the class. xUnit constructs a fresh instance of the test class for every test method, so a factory created in the constructor (and disposed after) yields a separate in-memory database per test, and tests cannot see each other's writes. Reach for `IClassFixture<T>` only when the tests in a class are read-only or seed uniquely-keyed data, since a shared factory means a shared database.

```csharp
public class MessagingApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<MessagingDbContext>>();
            services.AddDbContext<MessagingDbContext>(o => o.UseInMemoryDatabase($"test-{Guid.NewGuid()}"));
        });
}

public class NamespacesTests : IDisposable
{
    private readonly MessagingApiFactory _factory = new();
    private readonly HttpClient _client;

    public NamespacesTests() => _client = _factory.CreateClient();

    public void Dispose() => _factory.Dispose();
}
```

## Replace the database with an isolated one

`RemoveAll<DbContextOptions<MessagingDbContext>>()` is the key step: adding a second `AddDbContext` without removing the first leaves the app's original provider in place. The `Guid.NewGuid()` database name gives each factory its own store. With a per-test factory this means a clean database for every test, so tests never share state with each other or with the app.

## Seed exactly what each test asserts

A test must not depend on data seeded at app startup or by another test. Before acting, seed the specific rows this test needs through a scope from the factory's services; then call the endpoint and assert.

```csharp
using (var scope = _factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MessagingDbContext>();
    db.Namespaces.Add(new MessagingNamespace { Name = "ns-test", Location = "eastus" });
    await db.SaveChangesAsync();
}
```

❌ A test that expects 201 or 200 but relies on data the app seeded at startup or that another test created.
✅ Seed the row this test depends on first, so it passes in any order and on a fresh database.

## Assert status and payload

Assert the status code and the deserialized body, not merely that the call did not throw.

```csharp
var response = await _client.PostAsJsonAsync("/namespaces", new { name = "ns-1", location = "eastus", sku = "Standard" });
Assert.Equal(HttpStatusCode.Created, response.StatusCode);
var created = await response.Content.ReadFromJsonAsync<NamespaceResponse>();
Assert.Equal("ns-1", created!.Name);
```

❌ Asserting only `response.IsSuccessStatusCode`.
✅ Assert the exact status code and the values in the deserialized payload.

## Verify

- Tests obtain an `HttpClient` from `WebApplicationFactory<Program>`, and `Program` is reachable from the test project.
- The `DbContext` is replaced with an isolated per-factory database, with the app's original registration removed first.
- Each test seeds the data it asserts on through a factory-scoped `DbContext`, runs in any order, and asserts both the status code and the payload.
