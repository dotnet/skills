---
name: optimizing-ef-core-queries
description: "Optimize and improve the performance of slow Entity Framework Core (EF Core) queries: make them generate less SQL, make fewer database round-trips, and return results faster. Use whenever an EF Core or DbContext query or data-access path is slow or should be made faster — whether or not EF Core owns the database schema. For EF Core, not Dapper or raw ADO.NET."
license: MIT
---

# Optimizing EF Core Queries

Diagnose and fix slow Entity Framework Core (EF Core) queries. Start from the generated SQL/logs, apply the smallest change that removes the bottleneck, and confirm the fix by re-reading the SQL and the query count. Prefer changes that reduce round-trips, duplicated rows, scans, or per-call translation cost over micro-optimizations. Apply one change at a time and re-measure.

## When to Use

- EF Core queries are slow or emit far more SQL statements than expected
- The same query repeats once per row (N+1 / lazy loading)
- Multiple collection `Include`s blow up or duplicate rows
- Deep pages slow down as `Skip` grows, or bulk updates load rows just to modify them
- A filtered/sorted query scans **even though the column is indexed**, or a filtered/sorted column has no supporting index
- A hot, frequently-executed query pays EF Core's LINQ-translation cost on every call

## When Not to Use

- **The code uses Dapper or raw ADO.NET, not EF Core.** Answer the SQL/indexing/query-plan question directly; do not introduce a `DbContext` or recommend `AsNoTracking`, `Include`, `AsSplitQuery`, or other EF Core APIs.

## First: capture the generated SQL

You cannot optimize what you cannot see. Turn on command logging and read the SQL and query count before changing anything:

```csharp
optionsBuilder.LogTo(Console.WriteLine, LogLevel.Information);
// or set "Microsoft.EntityFrameworkCore.Database.Command": "Information" in appsettings.json
```

Tag a query with `.TagWith("...")` to find it in the log. Count how many statements a slow operation runs, and how many rows each returns, before and after each change.

## Fixes

### Keep predicates sargable — never wrap an indexed column in a function

An index can only be used when the indexed column appears **bare** on one side of the comparison. Wrapping it in a function or arithmetic — `CreatedAt.Year == y`, `CreatedAt.Date == d`, `ToLower(Name) == n`, `Price * 1.1 > x`, or a leading-wildcard `LIKE '%foo'` — forces a per-row computation the index cannot satisfy, so the query **scans the whole table even though the index exists**. Adding another index changes nothing. Rewrite the predicate so the column stays bare, usually as a half-open range:

```csharp
// Non-sargable: a function is computed for every row → full scan
db.Logs.Where(l => l.CreatedAt.Year == year);

// Sargable: bare column compared to constants → index seek
var start = new DateTime(year, 1, 1);
db.Logs.Where(l => l.CreatedAt >= start && l.CreatedAt < start.AddYears(1));
```

The same rule covers several common shapes:

- **Case-insensitive text** — compare a stored normalized column instead of `ToLower(...)`/`ToUpper(...)`.
- **Computed expressions** — compare against the precomputed constant, not `column * k > x`.
- **Converting the column to another type** — a predicate over `column.ToString()` (for example matching the *text form* of a number or date, `total.ToString().StartsWith(p)`) applies a function to every row and often can't be translated to SQL at all, forcing a client-side evaluation that pulls the whole table into memory. Filter on the typed column with a real comparison or range instead.
- **Substring search** — `name.Contains(term)` becomes an unanchored `LIKE '%term%'` that can't seek an index and scans the table; a trailing-wildcard prefix (`name.StartsWith(term)` → `'term%'`) can seek. Anchor the search when a prefix match is acceptable — this changes which rows match, so confirm the behavior first — and put real substring or fuzzy search behind a full-text index on large tables.

**Verify:** the plan shows a seek/index instead of a scan and duration drops. If the column genuinely has no index, add one (see below) — but only after the predicate is sargable.

### Compile hot, frequently-executed queries

On a very hot path that runs the *same* query shape thousands of times over a reused context, EF Core re-parses the LINQ expression tree and probes its query cache on every call. When the query is already minimal (an indexed lookup or a small projection) and read-only tweaks such as `AsNoTracking` buy nothing, that per-call translation is the remaining cost. Compile the query once with `EF.CompileQuery` / `EF.CompileAsyncQuery` and reuse the delegate:

```csharp
private static readonly Func<AppDbContext, int, ProductListItem> GetProduct =
    EF.CompileQuery((AppDbContext db, int id) =>
        db.Products.Where(p => p.Id == id)
                   .Select(p => new ProductListItem(p.Id, p.Name, p.Price))
                   .First());

public ProductListItem Lookup(AppDbContext db, int id) => GetProduct(db, id);
```

The delegate is `static` (compiled once) and takes the `DbContext` plus each parameter as arguments. Use it for endpoints or loops that execute one query shape at very high frequency; it does nothing for one-off queries.

**Verify:** the hot loop's mean time drops with identical results.

### Remove N+1 and avoid lazy loading

The same `SELECT` repeated once per row (a navigation accessed inside a loop) is an N+1. Load the related data in one round-trip — project the aggregates with `Select`, or eager-load with `Include`:

```csharp
var summaries = await db.Orders
    .Select(o => new OrderSummary(o.Id, o.Items.Count, o.Items.Sum(i => i.Price)))
    .ToListAsync();
```

Prefer projection or `Include` over lazy loading: lazy loading is a leading cause of N+1 and forces synchronous I/O. In server apps, don't enable `Microsoft.EntityFrameworkCore.Proxies` or mark navigations `virtual` for lazy loading.

**Verify:** a fixed, small query count regardless of row count.

### Split multiple collection Includes

`Include`ing two or more collection navigations in one query multiplies rows (a Cartesian explosion) and duplicates parent data. Use `AsSplitQuery()` so each collection loads in its own statement; add `OrderBy` on a unique key so rows stitch together:

```csharp
db.Blogs.Include(b => b.Posts).Include(b => b.Contributors).AsSplitQuery();
```

**Verify:** rows per statement drop sharply and total duration improves.

### Filter and paginate; prefer keyset over offset

Constrain large result sets with `Where`, and page with **keyset (seek)** pagination rather than `Skip`/`Take`, which still scans and discards the skipped rows on deep pages:

```csharp
db.Orders.Where(o => o.Id > lastSeenId).OrderBy(o => o.Id).Take(pageSize);
```

Order by a unique, stable, indexed key (add tie-breakers if the sort column isn't unique). Keyset pages by the *last key seen* rather than a page number, so it changes the method's inputs; when a fixed signature rules out an in-place switch, still flag the deep-offset scan and recommend keyset.

**Verify:** page latency stays roughly constant from early to deep pages.

### Add missing indexes

Moving a filter into SQL or making a predicate sargable stops the *client-side* waste, but a `WHERE` or `ORDER BY` on a column with no index still scans the whole table inside the database — and a frequently-run query then re-scans it on every call. So audit index coverage separately from the query rewrite: for each query, check whether its filter and sort columns are backed by an index. Entity keys and foreign keys are indexed by convention, but other columns — status flags, state/enum fields, timestamps, names — usually are **not** unless the model configures it. When a hot query filters or sorts on such an unindexed column, recommend adding an index and say so explicitly, even when the rewritten query already returns the right rows: the index is a separate fix the code change alone doesn't deliver. (If the predicate isn't sargable, fix that first — a new index can't help a scan caused by a function on the column.)

If EF Core owns the schema, add the index in the model and migrate:

```csharp
modelBuilder.Entity<Order>()
    .HasIndex(o => new { o.CustomerId, o.CreatedAt }); // equality column first, then range/sort
```

Then create the migration with `dotnet ef migrations add ...`. **Do not apply it** with `dotnet ef database update` (or any equivalent that writes to the database) without explicit user approval — applying a migration mutates the database, so add the migration, show it to the user, and let them run the update once they've reviewed it. If EF Core does not own the schema, recommend the same index to whoever manages the database. Don't over-index — every index slows writes.

**Verify:** the plan uses a seek/index instead of a scan.

### Set-based bulk updates and deletes

Replace a load-mutate-`SaveChanges` loop with `ExecuteUpdateAsync`/`ExecuteDeleteAsync` (EF Core 7+) — one statement, no entities materialized:

```csharp
await db.Products.Where(p => p.LastSoldDate < cutoff)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false));
```

These bypass the change tracker and EF-side cascade behavior — apply related changes explicitly.

**Verify:** a single `UPDATE`/`DELETE` with a `WHERE` and no preceding `SELECT`.

## Common Pitfalls

| Pitfall | Fix |
|---------|-----|
| Wrapping an indexed column in `.Year`/`.Date`/`ToLower`/arithmetic | Rewrite to a sargable range/comparison on the bare column |
| Adding an index to fix a scan on a non-sargable predicate | Fix the predicate first; the index can't help until the column is bare |
| Rewriting a filter into SQL but leaving a hot query on an unindexed column | The server-side scan is still a scan — recommend an index on the filter/sort column too |
| Compiling a query that runs only occasionally | Compile only genuinely hot, high-frequency query shapes |
| Lazy loading (proxies / `virtual` navigations) causing N+1 and forced sync I/O | Eager-load (`Include`) or project; keep queries async |
| `ToList()`/`AsEnumerable()` before `Where`/`Select` | Keep the query `IQueryable` so filtering/projection run in SQL |

## References

- [Efficient querying — EF Core](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying)
- [Compiled queries — EF Core](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#compiled-queries)
- [Efficient updating (ExecuteUpdate/ExecuteDelete) — EF Core](https://learn.microsoft.com/en-us/ef/core/performance/efficient-updating)
- [Single vs. split queries](https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries)
- [Pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination)
- [Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)
