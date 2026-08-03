---
name: minimal-api-data-access
description: >
  Read collections efficiently from minimal API handlers: keyset pagination over a deterministic total
  order, the next page delivered as an RFC 8288 Link header, reading without tracking, and projecting in
  the query.
  USE FOR: implementing a minimal API route handler that lists or queries a collection backed by EF Core;
  paging a large or frequently-changing collection correctly; ordering by a total order with a unique
  tiebreaker; keyset/cursor paging instead of Skip/OFFSET; delivering the next-page link via the Link
  header; reading with AsNoTracking and projecting to a DTO; computing counts in the query.
  DO NOT USE FOR: controller actions (use controller-data-access); endpoint result types and status codes
  (use author-minimal-api-endpoints); DTO shape design (use the model-payloads skills); optimistic
  concurrency or ETags (use minimal-api-concurrency); incremental change tracking / delta (use the
  change-tracking skill).
license: MIT
---

# Minimal API Data Access

A collection endpoint must return a bounded page, ordered by a deterministic total order, and hand the client the next page rather than make the client compute offsets. Returning the whole table grows unbounded, and offset paging silently breaks when the collection changes between requests.

## Page with a total order, a keyset cursor, and a Link header

- **Total order:** order by a key whose final component is unique (the primary key). If the sort column has ties, the database may break them differently between queries, so `Skip`/`Take` can repeat or skip rows across pages. End every `OrderBy` with `ThenBy(x => x.Id)`.
- **Keyset, not offset:** seek past the last item of the previous page with a `WHERE (sortKey, id) > (lastSortKey, lastId)` comparison instead of `Skip`/`OFFSET`. A value boundary does not shift when earlier rows are inserted or deleted, so pages do not duplicate or skip; it also seeks on the index instead of scanning `offset + limit` rows.
- **Immutable seek key:** sort and seek on an immutable key (the id, or an insertion-ordered column) so a row is not re-emitted if a mutable sort field changes after it was returned.
- **Link header (RFC 8288):** return the collection itself as the body and put the next page in the `Link` header with `rel="next"`; the client follows it opaquely. Omit the header on the last page.

```csharp
queues.MapGet("/", async Task<Ok<IReadOnlyList<QueueDto>>> (
    string namespaceName, HttpContext http, AppDbContext db, CancellationToken ct,
    string? cursor = null, int limit = 20) =>
{
    limit = Math.Clamp(limit, 1, 100); // bound the page size

    var source = db.Queues
        .AsNoTracking()
        .Where(q => q.Namespace.Name == namespaceName && !q.IsDeleted);

    if (Cursor.TryDecode(cursor, out var lastName, out var lastId))
    {
        // Seek past the previous page: (Name, Id) > (lastName, lastId).
        source = source.Where(q =>
            string.Compare(q.Name, lastName) > 0 || (q.Name == lastName && q.Id > lastId));
    }

    var rows = await source
        .OrderBy(q => q.Name).ThenBy(q => q.Id) // total order ending in the unique key
        .Take(limit + 1)                        // fetch one extra to detect a next page
        .Select(q => new QueueDto(q.Id, q.Name, q.Status))
        .ToListAsync(ct);

    var hasNext = rows.Count > limit;
    var items = hasNext ? rows.Take(limit).ToList() : rows;

    if (hasNext)
    {
        var last = items[^1];
        var next = Cursor.Encode(last.Name, last.Id);
        http.Response.Headers["Link"] = $"</namespaces/{namespaceName}/queues?cursor={next}&limit={limit}>; rel=\"next\"";
    }

    return TypedResults.Ok<IReadOnlyList<QueueDto>>(items); // the body is the bare collection
});
```

`Cursor` is a small helper that encodes the last row's `(Name, Id)` into an opaque token (for example base64url) and decodes it back; the client treats the token as a black box.

## Read without tracking, project, and count in the query

- Read with `AsNoTracking` and project to the DTO inside the query, so only the needed columns are fetched.
- When a scalar summary is needed (for example a count of children), compute it in the query with `CountAsync` or a projection; do not load a collection into memory just to count it.
- If clients need a total count, run a separate `CountAsync` and return it in an `X-Total-Count` header rather than forcing it into the body.

## Verify

- Results are ordered by a total order whose final key is unique (`ThenBy` the id), so paging cannot repeat or skip rows on ties.
- Paging is keyset (`(sortKey, id) > (lastSortKey, lastId)`) applied in the query, not `Skip`/`OFFSET`, and the page size is bounded.
- The next page is delivered as an RFC 8288 `Link` header with `rel="next"`, and the body is the collection itself.
- Reads use `AsNoTracking` and project to the response shape in the query; counts are computed in the query.

❌ `OrderBy(q => q.Name)` alone, then `Skip`/`Take` — a non-unique sort plus offset repeats or skips rows when the data changes.
✅ `OrderBy(q => q.Name).ThenBy(q => q.Id)` with a keyset seek and a `Link` header.

❌ Returning a page-number/offset envelope the client must assemble into the next request.
✅ Hand the client an opaque `next` link in the `Link` header.
