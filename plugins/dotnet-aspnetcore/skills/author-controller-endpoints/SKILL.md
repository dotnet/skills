---
name: author-controller-endpoints
description: >-
  Author ASP.NET Core controller-based Web API actions with correct result types and status codes. USE FOR: adding or editing controllers that derive from ControllerBase with [ApiController]; choosing an action's return type; returning 201 Created with a Location header, 204 No Content, 404, or 400 from controller actions; declaring produced status codes for OpenAPI; validating request input and query parameters. DO NOT USE FOR: minimal API route handlers (use author-minimal-api-endpoints); designing DTOs or entity mapping (use the model-payloads skills); EF Core querying or pagination internals (use the data-access skills); optimistic concurrency or ETags (use controller-concurrency); service-layer structure and the Result pattern (use structure-api-business-logic).
license: MIT
---

# Author Controller Endpoints

Write `[ApiController]` actions whose return type, status codes, and input validation are explicit and correct.

## Return ActionResult<T> with the typed helpers

Derive the controller from `ControllerBase`, annotate it with `[ApiController]` and attribute routes, and return `ActionResult<T>` using the status helpers (`Ok`, `CreatedAtAction`, `NotFound`, `NoContent`, `ValidationProblem`). `ActionResult<T>` lets an action return either a typed body or a status result, and it tells OpenAPI the success body type.

```csharp
[ApiController]
[Route("orders")]
public class OrdersController(StoreDbContext db) : ControllerBase
{
    [HttpGet("{id:int}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> GetOrder(int id) =>
        await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id) is Order order
            ? Ok(order.ToDto())
            : NotFound();
}
```

## Pick the right status code

| Operation | Helper | Status |
| --- | --- | --- |
| Create a resource | `CreatedAtAction(nameof(GetOrder), new { id = order.Id }, body)` | 201 + `Location` |
| Read | `Ok(body)`; `NotFound()` when absent | 200 / 404 |
| Update returning no body | `NoContent()` | 204 |
| Update returning the updated body | `Ok(body)` | 200 |
| Delete | `NoContent()`; `NotFound()` when absent | 204 / 404 |
| Invalid input | `ValidationProblem()` / `BadRequest(...)` | 400 |

A create returns **201 with a `Location`** header. `CreatedAtAction` builds the URL from a named GET action, so reference the actual get-by-id action.

❌ Returning the entity directly or `Ok(created)` from a create action loses the `Location` header and the 201 semantics.
✅ `return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order.ToDto());`

## Declare the produced status codes

`ActionResult<T>` describes the success body, but the non-success codes an action can return are invisible to OpenAPI unless declared. Add `[ProducesResponseType]` for each additional status the action produces.

```csharp
[HttpPost]
[ProducesResponseType(StatusCodes.Status201Created)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public async Task<ActionResult<OrderDto>> CreateOrder(CreateOrderRequest req)
{
    var order = Order.FromRequest(req);
    db.Orders.Add(order);
    await db.SaveChangesAsync();
    return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order.ToDto());
}

[HttpDelete("{id:int}")]
[ProducesResponseType(StatusCodes.Status204NoContent)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<IActionResult> DeleteOrder(int id)
{
    var order = await db.Orders.FindAsync(id);
    if (order is null)
    {
        return NotFound();
    }

    // Delete maps to 204 / 404 regardless of mechanism; a soft-deletable resource marks itself deleted rather than being removed.
    order.MarkDeleted();            // db.Orders.Remove(order) only when the resource is not soft-deletable
    await db.SaveChangesAsync();
    return NoContent();
}
```

## Validate input

With `[ApiController]`, a failed model validation automatically produces a 400 `ValidationProblemDetails` before the action body runs, so annotate request models and bound parameters with data annotations rather than hand-checking.

```csharp
public record ProductQuery
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    public int? CategoryId { get; init; }
}
```

❌ Reading `page`/`pageSize` straight into `Skip`/`Take` with no bounds, so `page = 0` or an unbounded size runs against the database.
✅ Constrain them with `[Range]` (auto-400) or check explicitly and return `ValidationProblem`.

## Verify

- Controllers derive from `ControllerBase` with `[ApiController]` and attribute routing.
- Actions return `ActionResult<T>`; creates use `CreatedAtAction` (201 + `Location`); deletes and empty updates return 204; missing resources return 404; invalid input returns 400 `ValidationProblemDetails`.
- Each non-success status an action can produce is declared with `[ProducesResponseType]`.
