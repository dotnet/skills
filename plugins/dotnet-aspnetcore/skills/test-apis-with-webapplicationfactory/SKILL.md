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

Add `Microsoft.AspNetCore.Mvc.Testing` to the test project (`dotnet add package Microsoft.AspNetCore.Mvc.Testing`) and a reference to the API project. A minimal API built from top-level statements has an inaccessible generated `Program`, so expose it: add `public partial class Program;` at the end of `Program.cs` (or use `[assembly: InternalsVisibleTo("YourTests")]` in the API project). Use xUnit's `IClassFixture<StoreApiFactory>` so the factory is shared across tests in the class, then call `factory.CreateClient()` to get an `HttpClient` wired to the in-process host.

```csharp
public class StoreApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<StoreDbContext>>();
            services.AddDbContext<StoreDbContext>(o => o.UseInMemoryDatabase($"test-{Guid.NewGuid()}"));
        });
}

public class OrdersTests(StoreApiFactory factory) : IClassFixture<StoreApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
}
```

## Replace the database with an isolated one

`RemoveAll<DbContextOptions<StoreDbContext>>()` is the key step: adding a second `AddDbContext` without removing the first leaves the app's original provider in place. The `Guid.NewGuid()` database name ensures each factory instance gets a separate store — tests never share state with each other or with the app.

## Seed exactly what each test asserts

A test must not depend on data seeded at app startup or by another test. Before acting, seed the specific rows this test needs through a scope from the factory's services; then call the endpoint and assert.

```csharp
using (var scope = factory.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
    db.Customers.Add(new Customer { Name = "Test", Email = "t@example.com" });
    await db.SaveChangesAsync();
}
```

❌ A test that expects 201 or 200 but relies on data the app seeded at startup or that another test created.
✅ Seed the row this test depends on first, so it passes in any order and on a fresh database.

## Assert status and payload

Assert the status code and the deserialized body, not merely that the call did not throw.

```csharp
var response = await client.PostAsJsonAsync("/orders", new { customerId = 1 });
Assert.Equal(HttpStatusCode.Created, response.StatusCode);
var order = await response.Content.ReadFromJsonAsync<OrderDto>();
Assert.Equal(1, order!.CustomerId);
```

❌ Asserting only `response.IsSuccessStatusCode`.
✅ Assert the exact status code and the values in the deserialized payload.

## Verify

- Tests obtain an `HttpClient` from `WebApplicationFactory<Program>`, and `Program` is reachable from the test project.
- The `DbContext` is replaced with an isolated per-factory database, with the app's original registration removed first.
- Each test seeds the data it asserts on through a factory-scoped `DbContext`, runs in any order, and asserts both the status code and the payload.
