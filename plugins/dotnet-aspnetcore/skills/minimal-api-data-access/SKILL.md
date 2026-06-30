---
name: minimal-api-data-access
description: >
  Read data efficiently from minimal API handlers, with bounded pagination on every collection endpoint.
  USE FOR: implementing a minimal API route handler that lists or queries a collection backed by EF Core;
  adding page and page-size query parameters with a bounded maximum; applying Skip/Take in the database
  query; ordering before paging; reading with AsNoTracking and projecting to a DTO in the query; computing
  counts in the query instead of loading collections.
  DO NOT USE FOR: controller actions (use controller-data-access); endpoint result types and status codes
  (use author-minimal-api-endpoints); DTO shape design (use the model-payloads skills); optimistic
  concurrency or ETags (use minimal-api-concurrency).
license: MIT
---

# Minimal API Data Access

A collection endpoint must page its results and read without tracking. Returning a whole table grows unbounded as the data grows.

## Page every collection endpoint

Take page and page-size as query parameters, bound the page size so a client cannot request everything, order the results, and apply `Skip`/`Take` in the query before it runs. Return the page together with the total count so the client can request the next one.

```csharp
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

queues.MapGet("/", async Task<Results<Ok<PagedResult<QueueDto>>, NotFound>> (
    string namespaceName, AppDbContext db, CancellationToken ct, int page = 1, int pageSize = 20) =>
{
    pageSize = Math.Clamp(pageSize, 1, 100); // never let an unbounded page through
    page = Math.Max(page, 1);

    var source = db.Queues
        .AsNoTracking()
        .Where(q => q.Namespace.Name == namespaceName && !q.IsDeleted);

    var total = await source.CountAsync(ct);

    var items = await source
        .OrderBy(q => q.Name)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(q => new QueueDto(q.Name, q.Status))
        .ToListAsync(ct);

    return TypedResults.Ok(new PagedResult<QueueDto>(items, page, pageSize, total));
});
```

- Clamp the page size to a maximum so an unbounded page cannot be requested.
- Order before `Skip`/`Take`: paging without a stable sort returns inconsistent pages.
- Read with `AsNoTracking` and project to the DTO inside the query, so only the needed columns are fetched.
- Compute counts with `CountAsync` in the query; do not load a collection into memory just to count it.

## Verify

- The list handler accepts a bounded page size and applies `Skip`/`Take` in the database query, not after materializing.
- Results are ordered before paging.
- Reads use `AsNoTracking` and project to the response shape in the query.

❌ `return TypedResults.Ok(await db.Queues.ToListAsync());` returns the entire table and grows without bound.
✅ Page the query with a bounded `Skip`/`Take`.

❌ `db.Queues.ToListAsync()` then `.Skip(...).Take(...)` in memory loads everything first.
✅ Apply `Skip`/`Take` on the `IQueryable` before `ToListAsync`.
