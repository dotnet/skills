---
name: optimizing-ef-core-queries
description: Optimize slow Entity Framework Core (EF Core) queries by fixing N+1 and lazy loading, projecting only needed columns, using AsNoTracking, splitting large Includes, paginating, adding indexes when EF Core owns the schema, and running set-based updates and deletes. Use when EF Core queries are slow, emit excessive or duplicated SQL, or cause high database CPU, IO, or memory. Not for Dapper or raw ADO.NET, or for database-side tuning of a schema EF Core does not manage.
license: MIT
---

# Optimizing EF Core Queries

Diagnose and fix slow Entity Framework Core (EF Core) queries. Work from the generated SQL, apply the smallest change that removes the bottleneck, and confirm the fix by re-reading the SQL and the query count. Prefer changes that reduce round-trips, rows, and columns over micro-optimizations.

## When to Use

- EF Core queries are slow or emit far more SQL statements than expected
- Logs show the same query repeated once per row (N+1)
- Database CPU/IO is high, or large result sets cause memory pressure
- A query returns many more rows or columns than the code actually uses

## When Not to Use

- The code uses Dapper or raw ADO.NET instead of EF Core
- The bottleneck is database-side **in a schema EF Core does not manage** — e.g. a DBA-owned or database-first schema where you cannot add indexes or change the model through EF Core migrations. When EF Core *does* own the schema, adding indexes and adjusting the model are in scope (see Step 7)
- You are designing a brand-new data layer from scratch — scaffold it first, then return here to tune real queries

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Slow EF Core query | Yes | The LINQ query, `DbContext` usage, or method to optimize |
| Generated SQL or logs | Recommended | EF Core SQL / command logs; capture them first (Step 1) if missing |
| Schema ownership | Recommended | Whether EF Core migrations own the schema (decides if Step 7 applies) |

## Symptom → fix

Route each symptom to the step that fixes it. Apply one change at a time and re-measure.

| What you see in the SQL/logs | Do this |
|------------------------------|---------|
| Same parameterized `SELECT` runs once per parent row | Step 2 — remove N+1 / lazy loading |
| Query selects many more columns than the code uses | Step 3 — project with `Select` |
| Read-only query, entities never updated | Step 4 — `AsNoTracking` |
| One query with several collection `Include`s returns a huge, duplicated row set | Step 5 — `AsSplitQuery` |
| Query returns thousands of rows the UI never shows | Step 6 — filter and paginate |
| `WHERE`/`ORDER BY` on an unindexed column does a scan (and EF Core owns the schema) | Step 7 — add an index |
| Code loads entities only to update or delete them in a loop | Step 8 — `ExecuteUpdate`/`ExecuteDelete` |
| High latency only under concurrent load; blocking calls | Step 9 — async + `DbContext` pooling |

## Workflow

Apply these in order, making one change at a time and re-reading the SQL after each:

1. Capture the generated SQL and count the queries.
2. Remove N+1 and stop relying on lazy loading.
3. Project to only the columns the caller uses.
4. Turn off change tracking for read-only queries.
5. Split multiple-collection `Include`s to avoid a Cartesian explosion.
6. Filter and paginate large result sets.
7. Add indexes for filtered/sorted columns (only when EF Core owns the schema).
8. Replace load-then-modify loops with set-based `ExecuteUpdate`/`ExecuteDelete`.
9. Make hot paths async and pool the `DbContext`.
10. Cache compiled queries only after proving query compilation is the bottleneck.
11. Drop to parameterized SQL only when LINQ can't express the query well.

### Step 1: Capture the generated SQL

You cannot optimize what you cannot see. Turn on command logging and read the SQL before changing anything.

```csharp
// DbContext configuration:
optionsBuilder
    .UseSqlServer(connectionString)
    .LogTo(Console.WriteLine, LogLevel.Information);
```

Or set the log category in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  }
}
```

Read the log and count queries. Use `.TagWith("...")` to label a query so you can find it in the log. Do not rely on `EnableSensitiveDataLogging()`/`EnableDetailedErrors()` for performance work — they expose parameter values and extra diagnostics but do not reveal query count, row count, or missing indexes.

**Verify:** you can point at the exact SQL statement(s) a slow operation runs and how many times each runs.

### Step 2: Remove N+1 and avoid lazy loading

**When you see** the same `SELECT` repeated once per row in the log (usually a navigation accessed inside a loop), you have an N+1 pattern.

**Before (N+1 — 1 query for orders, then 1 per order):**
```csharp
var orders = await db.Orders.ToListAsync();
foreach (var order in orders)
{
    var count = order.Items.Count; // each access lazy-loads Items
}
```

**After — load related data in one round-trip:**
```csharp
// Eager load with a JOIN
var orders = await db.Orders
    .Include(o => o.Items)
    .ToListAsync();

// Or project exactly what you need (usually best — see Step 3)
var summaries = await db.Orders
    .Select(o => new OrderSummary
    {
        OrderId = o.Id,
        ItemCount = o.Items.Count,
        Total = o.Items.Sum(i => i.Price)
    })
    .ToListAsync();
```

**Prefer eager loading or projection over lazy loading.** Lazy loading is a leading cause of N+1 *and* it forces synchronous I/O (it cannot be awaited), which hurts scalability even outside obvious loops. Avoid it in server apps: don't install/enable `Microsoft.EntityFrameworkCore.Proxies`, and don't mark navigations `virtual` for lazy loading. Load related data explicitly with `Include` or a projection instead.

**Verify:** the log shows a fixed, small number of queries regardless of row count.

### Step 3: Project only the columns you need

**When** a query materializes full entities but the code uses only a few properties, project to a DTO or anonymous type so EF Core selects fewer columns.

```csharp
// Before: loads every column, including large ones (Description, Blob, ...)
var products = await db.Products.Where(p => p.IsActive).ToListAsync();

// After: SELECT only what the caller uses
var products = await db.Products
    .Where(p => p.IsActive)
    .Select(p => new ProductListItem { Id = p.Id, Name = p.Name, Price = p.Price })
    .ToListAsync();
```

Projection cuts I/O, network, and memory, and it skips change tracking (projected types aren't tracked), so it is often the single biggest win for read paths. When you project, you do **not** need `Include` — the `Select` decides what is loaded.

**Verify:** the generated `SELECT` lists only the projected columns.

### Step 4: Turn off tracking for read-only queries

**When** results are only read (never updated in the same context), add `AsNoTracking()` to skip building change-tracking state.

```csharp
var products = await db.Products
    .AsNoTracking()
    .Where(p => p.IsActive)
    .ToListAsync();
```

For a read-heavy app, make it the default:

```csharp
services.AddDbContext<AppDbContext>(o => o
    .UseSqlServer(connectionString)
    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
```

Use `AsNoTrackingWithIdentityResolution()` when the same entity appears many times in the result and you want one instance per key.

**Verify:** read-only queries no longer pay tracking overhead; results are unchanged.

### Step 5: Split large Includes to avoid a Cartesian explosion

**When** a single query `Include`s two or more collection navigations, the JOIN multiplies rows (a Cartesian explosion) and duplicates parent data across the wire. Use `AsSplitQuery()` to fetch each collection in its own SQL statement.

```csharp
var blogs = await db.Blogs
    .Include(b => b.Posts)
    .Include(b => b.Contributors)
    .AsSplitQuery()
    .ToListAsync();
```

| Situation | Use |
|-----------|-----|
| Single collection `Include` | Single query (default) |
| Multiple collection `Include`s, or one large child collection | `AsSplitQuery()` |
| You need a consistent snapshot without a transaction | Single query |

Split queries trade one large result for several round-trips, and each runs separately (data can change between them unless wrapped in a transaction). Add `OrderBy` on a unique key so rows stitch together correctly. You can set split behavior globally with `UseQuerySplittingBehavior`.

**Verify:** row count per statement drops sharply and total duration improves.

### Step 6: Filter and paginate large result sets

**When** a query can return many rows, constrain it. Filter with `Where`, and page instead of returning everything. Prefer **keyset (seek) pagination** over `Skip`/`Take` (offset) pagination, which slows down as the offset grows because the database still scans and discards the skipped rows.

```csharp
// Offset pagination — degrades on deep pages
var page = await db.Orders.OrderBy(o => o.Id)
    .Skip(pageIndex * pageSize).Take(pageSize).ToListAsync();

// Keyset pagination — filter by the last key seen; stays fast at any depth
var page = await db.Orders
    .Where(o => o.Id > lastSeenId)
    .OrderBy(o => o.Id)
    .Take(pageSize)
    .ToListAsync();
```

Always order by a **unique, stable** key (add tie-breakers if the sort column isn't unique), and make sure that key is indexed (Step 7).

**Verify:** page latency is roughly constant across early and deep pages.

### Step 7: Add missing indexes (only when EF Core owns the schema)

Indexing is often the highest-impact fix. It applies here **only when EF Core manages the schema through migrations** — then adding an index is a model change, not out-of-scope DBA work.

**When** the database plan shows a scan (or a filtered/sorted column has no supporting index), add the index in the model and create a migration:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Order>()
        .HasIndex(o => o.CustomerId);              // single-column

    modelBuilder.Entity<Order>()
        .HasIndex(o => new { o.CustomerId, o.CreatedAt }); // composite: equality column first, then range/sort
}
```

```bash
dotnet ef migrations add AddOrderIndexes
dotnet ef database update
```

Order composite-index columns as equality predicates first, then the range/`ORDER BY` column. For queries that read a few extra columns, a covering index via `.IncludeProperties(...)` can avoid key lookups. Don't over-index — every index slows writes.

**Verify:** the query plan uses a seek/index instead of a scan, and duration drops. If EF Core does **not** own the schema, stop here and hand the plan to whoever manages the database.

### Step 8: Use set-based updates and deletes for bulk changes

**When** code loads entities only to modify or remove them, replace the load-mutate-`SaveChanges` loop with `ExecuteUpdateAsync`/`ExecuteDeleteAsync` (EF Core 7+). They run a single `UPDATE`/`DELETE` and never materialize entities.

```csharp
// Before: loads 500k rows into memory
var stale = await db.Products.Where(p => p.LastSoldDate < cutoff).ToListAsync();
foreach (var p in stale) p.IsActive = false;
await db.SaveChangesAsync();

// After: one UPDATE, no entities loaded
await db.Products
    .Where(p => p.LastSoldDate < cutoff)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false));

await db.AuditLogs
    .Where(l => l.CreatedAt < cutoff)
    .ExecuteDeleteAsync();
```

These bypass the change tracker, interceptors/`SaveChanges` events, and do not trigger EF-side cascade behavior — apply related changes explicitly.

**Verify:** the log shows a single `UPDATE`/`DELETE` with a `WHERE` and no preceding `SELECT`.

### Step 9: Use async and DbContext pooling for scalability

**When** the code path serves concurrent load (e.g. a web API), two low-code, high-impact changes help throughput:

- **Always use async query methods** — `ToListAsync`, `FirstOrDefaultAsync`, `SaveChangesAsync`, etc. — and `await` them. Blocking on synchronous database calls ties up thread-pool threads and causes starvation under load. Never block on async (`.Result`/`.Wait()`).
- **Enable `DbContext` pooling** to reuse context instances instead of allocating and configuring a new one per request:

```csharp
services.AddDbContextPool<AppDbContext>(o => o.UseSqlServer(connectionString));
```

Pooling resets context state between uses, so do **not** store per-request state (e.g. a tenant id set in the constructor) in a pooled context without accounting for the reset. Keep every `DbContext` short-lived and scoped per request; never cache or share one across requests or threads.

**Verify:** under concurrent load, latency and CPU drop and thread-pool starvation warnings disappear.

### Step 10: Cache compiled queries only on a proven hot path (last resort)

Compiled queries (`EF.CompileQuery`/`EF.CompileAsyncQuery`) remove the per-execution query-compilation overhead, but they help measurably only for complex queries on a proven hot path, and they add boilerplate. Reach for them only after you have measured that compilation — not execution — is the bottleneck; it is rarely the real problem.

```csharp
// Worth compiling only because this query is genuinely complex — 10+ chained
// operators — and runs on a hot path; a trivial single-predicate lookup would
// never repay the boilerplate.
private static readonly Func<AppDbContext, int, DateTime, Task<Order?>> GetLatestQualifyingOrder =
    EF.CompileAsyncQuery((AppDbContext db, int customerId, DateTime since) =>
        db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.CustomerId == customerId)
            .Where(o => o.CreatedAt >= since)
            .Where(o => o.Total > 0)
            .Where(o => o.Items.Any())
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Total)
            .ThenBy(o => o.Id)
            .FirstOrDefault());

var order = await GetLatestQualifyingOrder(db, customerId, since);
```

**Verify:** a profiler shows query compilation (not execution) dominating on a hot path before you add this, and the extra boilerplate buys a measurable win.

### Step 11: Drop to SQL for queries LINQ can't express efficiently

When a query is awkward or inefficient in LINQ, use `FromSql`/`FromSqlInterpolated` and keep it parameterized (never concatenate user input — that reopens SQL injection and defeats plan caching):

```csharp
var results = await db.Orders
    .FromSql($"SELECT * FROM Orders WHERE Total > {minTotal}")
    .AsNoTracking()
    .ToListAsync();
```

## Validation

- [ ] Captured the generated SQL and query count before and after (Step 1)
- [ ] No query repeats per row; related data loads via `Include` or projection, not lazy loading (Step 2)
- [ ] Queries select only the columns the caller uses (Step 3)
- [ ] Read-only queries use `AsNoTracking` (or a NoTracking default) (Step 4)
- [ ] Multiple-collection `Include`s use `AsSplitQuery` (Step 5)
- [ ] Large result sets are filtered and paginated, preferably keyset (Step 6)
- [ ] Filtered/sorted columns are indexed when EF Core owns the schema (Step 7)
- [ ] Bulk changes use `ExecuteUpdate`/`ExecuteDelete` (Step 8)
- [ ] Hot paths are fully async; `DbContext` is pooled and short-lived (Step 9)
- [ ] No new client-side-evaluation or multiple-collection-include warnings in the log

## Common Pitfalls

| Pitfall | Fix |
|---------|-----|
| Lazy loading (proxies / `virtual` navigations) causing N+1 and forced sync I/O | Use eager loading (`Include`) or projection; keep queries async |
| `ToList()`/`AsEnumerable()` before `Where`/`Select` | Keep the query `IQueryable` so filtering/projection run in SQL, not in memory |
| `Count() > 0` to test existence | Use `Any()` |
| `LIKE '%term%'` (leading wildcard, e.g. `Contains`) can't use a normal index | Anchor the pattern (`StartsWith`) or use full-text search for large tables |
| Global query filters skew a plan during analysis | Check `HasQueryFilter`; use `IgnoreQueryFilters()` when intentionally bypassing |
| Long-lived or shared `DbContext` | Scope it per request and combine with pooling (Step 9) |
| `FromSqlRaw` with concatenated input | Use `FromSql`/`FromSqlInterpolated` so values are parameterized |

## References

- [Efficient querying — EF Core](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying)
- [Efficient updating (ExecuteUpdate/ExecuteDelete) — EF Core](https://learn.microsoft.com/en-us/ef/core/performance/efficient-updating)
- [Single vs. split queries](https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries)
- [Tracking vs. no-tracking queries](https://learn.microsoft.com/en-us/ef/core/querying/tracking)
- [Pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination)
- [Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)
- [Advanced performance topics (DbContext pooling, compiled queries)](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics)
