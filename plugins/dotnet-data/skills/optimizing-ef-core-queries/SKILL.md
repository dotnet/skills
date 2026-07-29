---
name: optimizing-ef-core-queries
description: "Diagnose and fix non-trivial EF Core performance problems from SQL/logs: repeated SQL (N+1 / lazy loading), duplicated rows from multiple collection Includes, deep pagination, schema-owned indexing gaps, row-by-row bulk changes, and per-request DbContext setup cost. Use this when EF Core query behavior is slow or surprising; skip it for obvious one-line fixes the base model already handles well."
license: MIT
---

# Optimizing EF Core Queries

Diagnose and fix slow Entity Framework Core (EF Core) queries. Start from the generated SQL/logs, apply the smallest change that removes the bottleneck, and confirm the fix by re-reading the SQL and the query count. Prefer changes that reduce round-trips, duplicated rows, scans, or per-request setup cost over micro-optimizations.

## When to Use

- EF Core queries are slow or emit far more SQL statements than expected
- Logs show the same query repeated once per row (N+1 / lazy loading)
- Multiple collection `Include`s blow up row counts or duplicate parent data
- Deep pages get slower as `Skip` grows, or bulk updates load rows just to modify them
- A filtered/sorted query scans because EF Core owns the schema but the model lacks the supporting index
- A high-throughput path rebuilds `DbContext` instances per request

## When Not to Use

- The code uses Dapper or raw ADO.NET instead of EF Core
- The bottleneck is database-side **in a schema EF Core does not manage** — e.g. a DBA-owned or database-first schema where you cannot add indexes or change the model through EF Core migrations. When EF Core *does* own the schema, adding indexes and adjusting the model are in scope (see Step 6)
- You are designing a brand-new data layer from scratch — scaffold it first, then return here to tune real queries

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Slow EF Core query | Yes | The LINQ query, `DbContext` usage, or method to optimize |
| Generated SQL or logs | Recommended | EF Core SQL / command logs; capture them first (Step 2) if missing |
| Schema ownership | Recommended | Whether EF Core migrations own the schema (decides if Step 6 applies) |

## Workflow

### Step 1: Try the obvious direct fixes first

If the performance issue is already obvious from the code, try the smallest direct fix before doing deeper diagnosis:

- Add a `Select(...)` projection when the query materializes full entities but only a few columns are used
- Add `AsNoTracking()` on a clearly read-only query
- Replace `Count() > 0` with `Any()`
- Switch synchronous EF calls to `await`ed async methods on hot paths, and use `AddDbContextPool` when the context is safe to reuse
- Parameterize `FromSql` / `FromSqlInterpolated`

**Verify:** if one of these changes produces a significant measured improvement while preserving results, stop here. Use the remaining steps only when the bottleneck is still unclear or still present.

## Symptom → fix

Route each symptom to the step that fixes it. Apply one change at a time and re-measure.

| What you see in the SQL/logs | Do this |
|------------------------------|---------|
| Query selects many more columns than the code uses | Step 1 — direct fix with `Select` |
| Read-only query, entities never updated | Step 1 — direct fix with `AsNoTracking` |
| High latency only under concurrent load; blocking calls | Step 1 — direct fix with async methods + `DbContext` pooling |
| Same parameterized `SELECT` runs once per parent row | Step 3 — remove N+1 / lazy loading |
| One query with several collection `Include`s returns a huge, duplicated row set | Step 4 — `AsSplitQuery` |
| Query returns thousands of rows the UI never shows | Step 5 — filter and paginate |
| `WHERE`/`ORDER BY` on an unindexed column does a scan (and EF Core owns the schema) | Step 6 — add an index |
| Code loads entities only to update or delete them in a loop | Step 7 — `ExecuteUpdate`/`ExecuteDelete` |

Apply these in order, making one change at a time and re-reading the SQL after each:

1. Try the obvious direct fixes first; if one materially improves performance, stop.
2. Capture the generated SQL and count the queries.
3. Remove N+1 and stop relying on lazy loading.
4. Split multiple-collection `Include`s to avoid a Cartesian explosion.
5. Filter and paginate large result sets.
6. Add indexes for filtered/sorted columns (only when EF Core owns the schema).
7. Replace load-then-modify loops with set-based `ExecuteUpdate`/`ExecuteDelete`.

### Step 2: Capture the generated SQL

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

### Step 3: Remove N+1 and avoid lazy loading

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

// Or project exactly what you need (usually best — see Step 1)
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

### Step 4: Split large Includes to avoid a Cartesian explosion

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

### Step 5: Filter and paginate large result sets

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

Always order by a **unique, stable** key (add tie-breakers if the sort column isn't unique), and make sure that key is indexed (Step 6).

**Verify:** page latency is roughly constant across early and deep pages.

### Step 6: Add missing indexes (only when EF Core owns the schema)

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

### Step 7: Use set-based updates and deletes for bulk changes

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

## Validation

- [ ] Tried the obvious direct fixes first and stopped there if one materially improved the query (Step 1)
- [ ] Captured the generated SQL and query count before and after (Step 2)
- [ ] No query repeats per row; related data loads via `Include` or projection, not lazy loading (Step 3)
- [ ] Multiple-collection `Include`s use `AsSplitQuery` (Step 4)
- [ ] Large result sets are filtered and paginated, preferably keyset (Step 5)
- [ ] Filtered/sorted columns are indexed when EF Core owns the schema (Step 6)
- [ ] Bulk changes use `ExecuteUpdate`/`ExecuteDelete` (Step 7)

## Common Pitfalls

| Pitfall | Fix |
|---------|-----|
| Lazy loading (proxies / `virtual` navigations) causing N+1 and forced sync I/O | Use eager loading (`Include`) or projection; keep queries async |
| `ToList()`/`AsEnumerable()` before `Where`/`Select` | Keep the query `IQueryable` so filtering/projection run in SQL, not in memory |
| Global query filters skew a plan during analysis | Check `HasQueryFilter`; use `IgnoreQueryFilters()` when intentionally bypassing |

## References

- [Efficient querying — EF Core](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying)
- [Efficient updating (ExecuteUpdate/ExecuteDelete) — EF Core](https://learn.microsoft.com/en-us/ef/core/performance/efficient-updating)
- [Single vs. split queries](https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries)
- [Pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination)
- [Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)
- [Advanced performance topics (DbContext pooling)](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics)
