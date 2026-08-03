---
name: author-minimal-api-endpoints
description: >
  Author ASP.NET Core Minimal API endpoints with correct HTTP result types and status codes.
  USE FOR: adding or editing app.MapGet/MapPost/MapPut/MapDelete route handlers; choosing the
  return type of a minimal API endpoint; returning 201 Created, 204 No Content, 404, or 400 from
  minimal APIs; validating request input and returning ProblemDetails; making endpoint responses
  strongly typed so OpenAPI can describe them.
  DO NOT USE FOR: MVC or controller actions and [ApiController] (use author-controller-endpoints);
  designing DTOs or entity-to-DTO mapping (use model-minimal-api-payloads); EF Core querying,
  pagination internals, or DbContext setup (use minimal-api-data-access); optimistic concurrency or
  ETags (use minimal-api-concurrency); content negotiation, XML formatters, or file uploads.
license: MIT
---

# Author Minimal API Endpoints

Write minimal API route handlers whose return type, status codes, and input validation are explicit and correct.

## Return typed results

Declare each handler's return type as a `Results<...>` union of the concrete result types it can produce, and construct every result with `TypedResults.*` (never the untyped `Results.*`). The union gives compile-time checking that the handler only returns statuses it declares, and it emits response-type metadata so OpenAPI describes every outcome automatically.

```csharp
group.MapGet("/{id:int}", async Task<Results<Ok<Order>, NotFound>> (int id, StoreDbContext db) =>
    await db.Orders.FindAsync(id) is Order order
        ? TypedResults.Ok(order)
        : TypedResults.NotFound());
```

When a handler has exactly one outcome, return the concrete type directly (`Task<Ok<Order>>`); reach for the union only when more than one status is reachable.

## Pick the right status code

| Operation | Result | Status |
| --- | --- | --- |
| Create a resource | `TypedResults.Created(uri, body)` or `TypedResults.CreatedAtRoute(body, routeName, values)` | 201 + `Location` |
| Read | `TypedResults.Ok(body)`; `TypedResults.NotFound()` when absent | 200 / 404 |
| Update returning no body | `TypedResults.NoContent()` | 204 |
| Update returning the updated body | `TypedResults.Ok(body)` | 200 |
| Delete | `TypedResults.NoContent()`; `NotFound()` when absent | 204 / 404 |
| Invalid input | `TypedResults.ValidationProblem(errors)` or `BadRequest(...)` | 400 |

A create must return **201 with a `Location`** header pointing at the new resource. Name the GET route (`.WithName("GetOrder")`) and reference it from `CreatedAtRoute`.

For a create-or-update against a known URL (for example a one-to-one nested resource at `/customers/{id}/address`), use `PUT` and make it idempotent: return 204 (or 200) whether the resource was created or updated; reserve 201 for the case where you mint a brand-new sub-resource and return its location.

## Validate input, return ProblemDetails

Validate the request before touching the database and return `TypedResults.ValidationProblem(...)` (an RFC 7807 problem document) for bad input, listed in the handler's union. Guard query parameters the same way: reject a non-positive page or an oversized page size rather than letting `Skip`/`Take` run with bad values.

```csharp
group.MapPost("/{orderId:int}/items",
    async Task<Results<CreatedAtRoute<OrderItem>, NotFound, ValidationProblem>> (
        int orderId, AddItemRequest req, StoreDbContext db) =>
{
    if (req.Quantity < 1)
    {
        return TypedResults.ValidationProblem(new Dictionary<string, string[]>
        {
            ["quantity"] = ["Quantity must be at least 1."]
        });
    }

    if (await db.Orders.FindAsync(orderId) is null || await db.Products.FindAsync(req.ProductId) is null)
    {
        return TypedResults.NotFound();
    }

    var item = new OrderItem { OrderId = orderId, ProductId = req.ProductId, Quantity = req.Quantity };
    db.OrderItems.Add(item);
    await db.SaveChangesAsync();
    return TypedResults.CreatedAtRoute(item, "GetOrder", new { id = orderId });
});
```

## Verify

- `dotnet build` succeeds.
- Every handler's declared `Results<...>` union lists exactly the statuses it returns (the compiler enforces this once the return type is declared).
- Creates return 201 with a `Location`; deletes and empty updates return 204; missing resources return 404; invalid input returns 400 with a ProblemDetails body.
