---
name: controller-concurrency
description: >
  Add optimistic concurrency and HTTP conditional requests to ASP.NET Core controller actions, using
  both validators a resource can offer.
  USE FOR: protecting controller updates against concurrent edits and lost updates; exposing an ETag
  and a Last-Modified header from a resource; honoring If-Match and If-None-Match (the content/ETag
  validator) and If-Modified-Since and If-Unmodified-Since (the time/Last-Modified validator);
  returning 304, 412, or 428; handling DbUpdateConcurrencyException.
  DO NOT USE FOR: minimal API route handlers (use minimal-api-concurrency); basic action result types
  and status codes (use author-controller-endpoints); general EF Core querying (use the data-access
  skills); service-layer structure (use structure-api-business-logic).
license: MIT
---

# Controller Concurrency and Conditional Requests

A resource can carry two independent validators, and a complete implementation offers both: an **ETag** for content and an **Last-Modified** for time. Emit both on reads and honor both on writes.

- **ETag** comes from the concurrency token (a database rowversion mapped with `[Timestamp]`, or an app-managed version). It changes only when the resource's content changes. It drives `If-Match` (write precondition: stale write to 412) and `If-None-Match` (read: unchanged to 304; `*` means create-only).
- **Last-Modified** comes from the resource's last-modified timestamp. It is the time-based validator. It drives `If-Modified-Since` (read: not changed since to 304) and `If-Unmodified-Since` (write precondition: changed since to 412).

A resource that has a last-modified timestamp must offer `Last-Modified`, not only an `ETag`. The two answer different questions ("is it the exact same version" versus "has it changed since this time") and clients rely on each.

## One helper that reads both validators off the entity

```csharp
public static class ConditionalRequest
{
    public static string ETag(byte[] rowVersion)
    {
        return $"\"{Convert.ToBase64String(rowVersion)}\"";
    }

    public static string LastModified(DateTimeOffset lastModifiedAt)
    {
        return lastModifiedAt.ToString("R");
    }

    public static void WriteValidators(HttpResponse response, byte[] rowVersion, DateTimeOffset lastModifiedAt)
    {
        response.Headers.ETag = ETag(rowVersion);
        response.Headers.LastModified = LastModified(lastModifiedAt);
    }

    // A read can answer 304 when the client's validators still match the current state.
    public static bool IsNotModified(HttpRequest request, byte[] rowVersion, DateTimeOffset lastModifiedAt)
    {
        var ifNoneMatch = request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrEmpty(ifNoneMatch))
        {
            return string.Equals(ifNoneMatch, ETag(rowVersion), StringComparison.Ordinal);
        }

        if (DateTimeOffset.TryParse(request.Headers.IfModifiedSince, out var since))
        {
            // HTTP-date has one-second resolution.
            return lastModifiedAt <= since.AddSeconds(1);
        }

        return false;
    }

    // A write must be refused with 412 when the client based it on a stale copy.
    public static bool PreconditionFailed(HttpRequest request, byte[] rowVersion, DateTimeOffset lastModifiedAt)
    {
        var ifMatch = request.Headers.IfMatch.ToString();
        if (!string.IsNullOrEmpty(ifMatch) && !string.Equals(ifMatch, "*", StringComparison.Ordinal))
        {
            return !string.Equals(ifMatch, ETag(rowVersion), StringComparison.Ordinal);
        }

        if (DateTimeOffset.TryParse(request.Headers.IfUnmodifiedSince, out var limit))
        {
            return lastModifiedAt > limit.AddSeconds(1);
        }

        return false;
    }

    // True when the client supplied no write precondition at all; an endpoint that
    // requires one answers 428 Precondition Required instead of writing blindly.
    public static bool HasNoPrecondition(HttpRequest request)
    {
        return string.IsNullOrEmpty(request.Headers.IfMatch)
            && string.IsNullOrEmpty(request.Headers.IfUnmodifiedSince);
    }
}
```

Compare ETag and header tokens with `StringComparison.Ordinal`, never the culture-sensitive default.

## Conditional GET

```csharp
[HttpGet("{id:int}")]
public async Task<ActionResult<ResourceDto>> Get(int id, CancellationToken ct)
{
    var resource = await db.Resources.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    if (resource is null)
    {
        return NotFound();
    }

    if (ConditionalRequest.IsNotModified(Request, resource.RowVersion, resource.LastModifiedAt))
    {
        return StatusCode(StatusCodes.Status304NotModified);
    }

    ConditionalRequest.WriteValidators(Response, resource.RowVersion, resource.LastModifiedAt);
    return Ok(resource.ToDto());
}
```

## Conditional update

```csharp
[HttpPut("{id:int}")]
public async Task<ActionResult<ResourceDto>> Update(int id, UpdateResourceRequest request, CancellationToken ct)
{
    var resource = await db.Resources.FindAsync([id], ct);
    if (resource is null)
    {
        return NotFound();
    }

    // An endpoint that requires a precondition refuses a blind write.
    if (ConditionalRequest.HasNoPrecondition(Request))
    {
        return StatusCode(StatusCodes.Status428PreconditionRequired);
    }

    if (ConditionalRequest.PreconditionFailed(Request, resource.RowVersion, resource.LastModifiedAt))
    {
        return StatusCode(StatusCodes.Status412PreconditionFailed);
    }

    Apply(resource, request);
    resource.LastModifiedAt = DateTimeOffset.UtcNow; // bump the time validator on every content change

    try
    {
        await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateConcurrencyException)
    {
        // Two writers raced past the header check; the rowversion in the UPDATE caught it.
        return StatusCode(StatusCodes.Status412PreconditionFailed);
    }

    ConditionalRequest.WriteValidators(Response, resource.RowVersion, resource.LastModifiedAt);
    return Ok(resource.ToDto());
}
```

The header check rejects the obvious stale write; the tracked entity's rowversion still backstops the race at `SaveChanges`, raising `DbUpdateConcurrencyException`. Catch it and map it to 412 (or 409 for an endpoint with no conditional header); never let it surface as a 500.

## Verify

- A read emits **both** `ETag` and `Last-Modified`; `If-None-Match` returns 304 and `If-Modified-Since` returns 304, each independently.
- A write honors **both** `If-Match` (412 when stale) and `If-Unmodified-Since` (412 when changed since); a write that requires a precondition but receives none returns 428 Precondition Required.
- A 304 response has no body.
- The `ETag` is quoted and reflects the rowversion (content); `Last-Modified` reflects the timestamp; ETag comparisons are ordinal.
- A `SaveChanges` race is caught as `DbUpdateConcurrencyException` and returned as 412 or 409.

❌ Offering only an `ETag` when the resource also carries a last-modified timestamp.
✅ Emit and honor both the `ETag` and `Last-Modified` validators.

❌ Comparing ETags or header values with the culture-sensitive default `==`.
✅ `string.Equals(..., StringComparison.Ordinal)`.

❌ Checking the precondition header but not handling the `SaveChanges` race.
✅ Catch `DbUpdateConcurrencyException` as well.
