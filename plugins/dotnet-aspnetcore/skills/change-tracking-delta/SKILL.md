---
name: change-tracking-delta
description: >
  Add incremental change tracking (delta) to a collection API so a client can sync a local copy and then
  fetch only what changed - added, updated, and removed - since its last sync, without re-downloading the
  whole collection.
  USE FOR: an endpoint that returns changes since a client's last snapshot; a change-tracking / delta link
  the client returns with; reporting deletions to the client (tombstones) rather than letting them
  silently vanish; choosing and safely advancing a change watermark (rowversion or timestamp); a
  multipart change response that keeps entity payloads unchanged; conveying no-changes; expiring a stale
  token.
  DO NOT USE FOR: ordinary forward pagination of a collection (use the data-access skills); optimistic
  concurrency on a single resource (use the concurrency skills); real-time push/streaming.
license: MIT
---

# Change Tracking (Delta)

After an initial sync, a client should be able to ask for only what changed since last time - entities **added**, **updated**, and **removed** - instead of re-reading the whole collection. This is keyset pagination whose ordering key is a per-entity **change marker**: the client holds an opaque token for "the last change I saw," and the server returns everything past it, then a fresh token.

## What the collection must provide

- **A monotonic change marker per entity** that advances on every create, update, **and** soft-delete, so "changed since token" is a queryable range. A database **rowversion** is the right default: it is assigned and bumped by the database on every write, so it is monotonic without app code.
- **Soft delete** (`IsDeleted` + `DeletedAt`), with the change marker bumped at deletion time. A hard delete erases the row, so the delta can never *report* the deletion - the client would keep a phantom. Deleted rows are retained as tombstones.

## Map the rowversion as a comparable marker

A SQL Server rowversion is stored as 8 bytes, which C# cannot range-compare with `>`. Map it to a `ulong` with an **order-preserving** (big-endian) converter so a range query over it is valid and monotonic.

```csharp
public class Contact
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public ulong Version { get; set; } // the row's change marker (a rowversion)
}

// OnModelCreating: rowversion column, exposed as an order-preserving number.
modelBuilder.Entity<Contact>()
    .Property(c => c.Version)
    .IsRowVersion()
    .HasConversion(new NumberToBytesConverter<ulong>());
```

## The query: everything past the watermark, in total order

Decode the client's token to a watermark, return rows whose marker is beyond it - **including soft-deleted ones** so removals surface - ordered by the marker with a unique tiebreaker, and page with keyset.

```csharp
var changed = await db.Contacts
    .AsNoTracking()
    .Where(c => c.Version > watermark)          // include soft-deleted: do not filter IsDeleted here
    .OrderBy(c => c.Version).ThenBy(c => c.Id)  // total order over the change marker
    .Take(limit + 1)                            // one extra to detect another page
    .ToListAsync(ct);
```

If your provider cannot translate `>` on the mapped rowversion, run the range predicate as raw SQL (`FromSql($"... WHERE [Version] > {watermark}")`); the ordering is the same.

## The response: multipart, so entity payloads stay unchanged

Return the changes as a `multipart/mixed` body whose parts are each an `application/http` sub-response (RFC 9112). An added or updated entity is a `200 OK` part carrying its **normal representation**; a removed entity is a body-less `204 No Content` part - the client reads "body present" as upsert and "no body" as remove, and `Content-Location` identifies which resource. This keeps every part in the success range (a `204` mirrors a successful `DELETE`) rather than using a `4xx` for a resource that is legitimately gone. Nothing is wrapped and no entity gains a `removed` field. The change-tracking link travels in the response `Link` header.

Add and update are both `200` with the current representation, so the client upserts (no local copy means add, otherwise update). Distinguishing them is only possible, and only worth it, if you keep a **creation marker separate from the last-change marker**: then you may return `201 Created` for an entity whose creation is past the client's watermark and `200` for one merely updated. Without that separate marker, `200` covers both.

```csharp
public sealed record DeltaItem(string SelfUrl, bool Removed, object? Body);

public static class DeltaResponse
{
    public static async Task WriteAsync(
        HttpResponse response, IReadOnlyList<DeltaItem> items, string deltaLink, string? nextLink, CancellationToken ct)
    {
        var boundary = "delta_" + Guid.NewGuid().ToString("N");
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = $"multipart/mixed; boundary=\"{boundary}\"";

        // rel="deltaLink": come back here for the next round; rel="next": more pages in this delta.
        var link = $"<{deltaLink}>; rel=\"deltaLink\"";
        if (nextLink is not null)
        {
            link += $", <{nextLink}>; rel=\"next\"";
        }

        response.Headers["Link"] = link;

        await using var writer = new StreamWriter(response.Body, new UTF8Encoding(false), leaveOpen: true);
        foreach (var item in items)
        {
            await writer.WriteAsync($"\r\n--{boundary}\r\nContent-Type: application/http\r\n\r\n");
            if (item.Removed)
            {
                // No body: 204 keeps the part in the success range and reads as a DELETE-style outcome.
                await writer.WriteAsync($"HTTP/1.1 204 No Content\r\nContent-Location: {item.SelfUrl}\r\n\r\n");
            }
            else
            {
                var json = JsonSerializer.Serialize(item.Body);
                await writer.WriteAsync(
                    $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Location: {item.SelfUrl}\r\n\r\n{json}\r\n");
            }
        }

        await writer.WriteAsync($"\r\n--{boundary}--\r\n");
    }
}
```

## The endpoint

An absent token means the first sync (watermark of zero); a present-but-unreadable or expired token means the client must start over (`410`). Hand back a `deltaLink` carrying the new high-water mark; when the delta itself spans pages, also emit a `next` link that continues from where this page ended.

```csharp
[HttpGet("delta")]
public async Task<IActionResult> Delta(string? token, CancellationToken ct)
{
    ulong watermark = 0;
    if (token is not null && !DeltaToken.TryDecode(token, out watermark))
    {
        return StatusCode(StatusCodes.Status410Gone); // stale/unknown token: client does a full resync
    }

    const int limit = 100;
    var changed = await db.Contacts
        .AsNoTracking()
        .Where(c => c.Version > watermark)
        .OrderBy(c => c.Version).ThenBy(c => c.Id)
        .Take(limit + 1)
        .ToListAsync(ct);

    var hasNext = changed.Count > limit;
    var page = hasNext ? changed.Take(limit).ToList() : changed;
    var newWatermark = page.Count > 0 ? page[^1].Version : watermark;

    var items = page
        .Select(c => new DeltaItem($"/contacts/{c.Id}", c.IsDeleted, c.IsDeleted ? null : c.ToDto()))
        .ToList();

    var deltaLink = $"/contacts/delta?token={DeltaToken.Encode(newWatermark)}";
    var nextLink = hasNext ? deltaLink : null;

    await DeltaResponse.WriteAsync(Response, items, deltaLink, nextLink, ct);
    return new EmptyResult(); // the body is already written
}
```

A minimal API handler is identical apart from the entry point: take `string? token`, `HttpContext http`, call `DeltaResponse.WriteAsync(http.Response, ...)`, and `return Results.Empty;`.

**No changes** falls out naturally: `changed` is empty, so the body is an empty multipart and the `Link` header still carries a refreshed `deltaLink` at the same watermark - the client learns "nothing new, come back with this."

## What it looks like on the wire

One added or updated resource (both `200` with the representation) and one removed resource (`204`, no body):

```http
HTTP/1.1 200 OK
Content-Type: multipart/mixed; boundary="delta_9f2c1a7b"
Link: </contacts/delta?token=eyJ2IjoiQTNGMiJ9>; rel="deltaLink"

--delta_9f2c1a7b
Content-Type: application/http

HTTP/1.1 200 OK
Content-Location: /contacts/1042
Content-Type: application/json
ETag: "0x000000000000A3E1"

{"id":1042,"name":"Ada Lovelace","email":"ada@example.com"}

--delta_9f2c1a7b
Content-Type: application/http

HTTP/1.1 204 No Content
Content-Location: /contacts/993

--delta_9f2c1a7b--
```

When the change set spans pages, every page but the last carries `rel="next"`; only the final page carries `rel="deltaLink"`. A no-changes response is just the closing boundary plus a refreshed `deltaLink`. The optional `ETag` on a `200` part is the resource's rowversion, letting the client do conditional requests on it later.

## Advancing the watermark safely

The token is **opaque** to the client (an encoded watermark); only the server reads it. The subtlety is not losing changes at the boundary.

Rowversion is strictly monotonic, but a long transaction can be *assigned* a low rowversion yet *commit later* than a reader who already advanced the watermark, so a naive `> watermark` next time skips it. Do not hand out a watermark above the oldest still-open write: cap it at `MIN_ACTIVE_ROWVERSION()` on SQL Server, or overlap slightly and dedup by id. Always order by `(marker, id)` so the watermark advances deterministically.

## When rowversion is not available: a timestamp watermark

On a store without a rowversion, use a `LastModifiedAt` timestamp that **application code sets on every create, update, and soft-delete**, and treat it as the marker. It is portable but weaker, so add safeguards:

- **Overlap and dedup:** clock skew and coarse resolution mean two rows can share a boundary value, so query `c.LastModifiedAt >= watermark` (not `>`) with a small safety overlap, and have the client **dedup by id**. Keep the `(LastModifiedAt, id)` tiebreaker so a tie is not split across a page boundary.
- **Set it consistently:** every write path, including soft-delete, must stamp `LastModifiedAt` (ideally from a single server clock), or changes are missed.

Everything else - soft-delete tombstones, the multipart response, the `deltaLink`, no-changes, `410` on a stale token - is the same.

## Standards basis

This uses only general HTTP: `multipart/mixed` (RFC 2046) of `application/http` messages (RFC 9112), the `Link` header with `rel="next"`/`rel="self"` and an extension `rel="deltaLink"` (RFC 8288), `204 No Content` for a removed resource (a DELETE-style outcome, kept in the success range), and `410 Gone` for an expired change-tracking token. A simpler, less strict alternative keeps a bare JSON array and marks deletions inline (`{ "id": "...", "removed": true }`) with the links in the `Link` header; prefer that only when a multipart body is impractical for the client.

## Verify

- Changes are found by a monotonic per-entity change marker - a rowversion by default (mapped order-preserving so `> watermark` is valid), or an app-maintained timestamp when no rowversion exists - returning only entities past the client's watermark, not a full rescan.
- Deletions are reported: soft-deleted rows are included and surface as body-less `204 No Content` tombstones (the client removes them), so the client stops keeping them; a hard delete would make the deletion invisible.
- The client gets an opaque change-tracking link back via the `Link` header (`rel="deltaLink"`), and a `next` link when the delta spans pages.
- The delta is ordered by `(marker, id)`; the watermark advances deterministically and the boundary hazard is handled (rowversion in-flight commits via `MIN_ACTIVE_ROWVERSION`; timestamp via `>=` overlap and dedup).
- No-changes returns an empty delta plus a refreshed `deltaLink`; a stale/unknown token returns `410 Gone` so the client resyncs.
- Entity payloads are unchanged - added/updated entities carry their normal representation, not an envelope or an added field.

❌ Query `LastModifiedAt > since` with no tiebreaker, hard deletes, and a body that just omits removed rows - the client never learns about deletions and can miss changes at the boundary.
✅ A monotonic marker ordered `(marker, id)`, soft-delete tombstones reported as body-less `204` parts, an opaque `deltaLink`, and boundary-safe watermark advancement.
