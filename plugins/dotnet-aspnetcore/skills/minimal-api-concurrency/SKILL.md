---
name: minimal-api-concurrency
description: >
  Add optimistic concurrency and HTTP conditional requests to ASP.NET Core minimal API endpoints.
  USE FOR: protecting updates against concurrent edits and lost updates; adding a concurrency token
  (a database rowversion / [Timestamp], or an app-managed [ConcurrencyCheck] version) to an entity;
  emitting and validating ETag headers; honoring If-None-Match with 304 Not Modified and If-Match with
  412 Precondition Failed; handling DbUpdateConcurrencyException; conditional GET and conditional
  update handlers.
  DO NOT USE FOR: basic endpoint result types and status codes (use author-minimal-api-endpoints);
  controller-based APIs (use controller-concurrency); general EF Core querying or pagination (use the
  data-access skills); service-layer structure and the Result pattern (use
  structure-api-business-logic).
license: MIT
---

# Minimal API Concurrency and Conditional Requests

Drive both optimistic concurrency and HTTP conditional requests from one concurrency token: expose it as an `ETag`, validate it on reads with `If-None-Match` and on writes with `If-Match`, and let EF Core enforce it on save.

## Add a concurrency token and derive the ETag from it

Give the entity a concurrency token. On a relational database use a rowversion; on a provider without one (for example the in-memory provider) use an application-managed token that you change on every update.

```csharp
public class Product
{
    // ...
    [Timestamp] public byte[] RowVersion { get; set; } = [];   // relational rowversion
    // Provider without rowversion: [ConcurrencyCheck] public Guid Version { get; set; }  (assign Guid.NewGuid() on each update)
}
```

The ETag is the token, base64-encoded and quoted per the header format:

```csharp
static string ETagFor(byte[] rowVersion) => $"\"{Convert.ToBase64String(rowVersion)}\"";
```

## Conditional GET: ETag plus If-None-Match returns 304

Emit the `ETag` on the response. When the request's `If-None-Match` already matches the current token, return 304 Not Modified with no body; otherwise return 200 with the body and the current `ETag`.

```csharp
products.MapGet("/{id:int}", async Task<Results<Ok<ProductDto>, NotFound, StatusCodeHttpResult>> (
    int id, HttpContext http, StoreDbContext db) =>
{
    var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
    if (product is null) return TypedResults.NotFound();

    var etag = ETagFor(product.RowVersion);
    http.Response.Headers.ETag = etag;
    if (http.Request.Headers.IfNoneMatch == etag)
        return TypedResults.StatusCode(StatusCodes.Status304NotModified);

    return TypedResults.Ok(product.ToDto());
});
```

## Conditional update: If-Match returns 412 when stale

Require `If-Match` on an update that must not clobber a newer change. When it is missing, return 428 Precondition Required; when it is stale, return 412 Precondition Failed and do not change anything; when it matches, apply the change.

```csharp
products.MapPut("/{id:int}/price", async Task<Results<NoContent, NotFound, StatusCodeHttpResult>> (
    int id, UpdatePriceRequest req, HttpContext http, StoreDbContext db) =>
{
    var ifMatch = http.Request.Headers.IfMatch.ToString();
    if (string.IsNullOrEmpty(ifMatch))
        return TypedResults.StatusCode(StatusCodes.Status428PreconditionRequired);

    var product = await db.Products.FindAsync(id);
    if (product is null) return TypedResults.NotFound();

    if (ETagFor(product.RowVersion) != ifMatch)
        return TypedResults.StatusCode(StatusCodes.Status412PreconditionFailed);

    product.Price = req.Price;
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateConcurrencyException)
    {
        return TypedResults.StatusCode(StatusCodes.Status412PreconditionFailed);
    }

    http.Response.Headers.ETag = ETagFor(product.RowVersion);
    return TypedResults.NoContent();
});
```

Two concurrent requests can both pass the header check and then race at `SaveChanges`. For a tracked entity loaded via `FindAsync`, EF Core automatically includes the rowversion in the SQL `UPDATE`; a concurrent change causes zero rows to match and throws `DbUpdateConcurrencyException`. Map that exception to 412 for an `If-Match` flow, or 409 Conflict for an endpoint that takes concurrent edits without a conditional header. Never let it surface as a 500.

## Verify

- The entity has a concurrency token and the GET emits a quoted `ETag`.
- `If-None-Match` returns 304 with no body when the token is unchanged.
- `If-Match` returns 412 when stale, 428 when required but missing, and applies the change when current.
- A `SaveChanges` race is caught as `DbUpdateConcurrencyException` and returned as 412 or 409, never a 500.

❌ Updating a record without checking any version (last-writer-wins silently loses the other user's change).
✅ Require `If-Match` or a concurrency token and reject stale writes.

❌ Pre-checking the version in the handler but not handling the `SaveChanges` race.
✅ Catch `DbUpdateConcurrencyException` after the header pre-check; for a tracked entity loaded via `FindAsync`, EF Core enforces the rowversion in the SQL `UPDATE` automatically.

❌ An unquoted `ETag` value.
✅ Quote the validator per the header format.
