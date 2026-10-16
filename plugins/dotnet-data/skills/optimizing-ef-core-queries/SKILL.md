---
name: optimizing-ef-core-queries
description: "Review and optimize application data access that uses Entity Framework Core (EF Core). Use for EF Core performance and correctness reviews of services, repositories, imports, and background jobs: slow or memory-heavy queries, excessive round-trips, hidden SaveChanges, premature materialization, dynamic query construction, result-graph tracking, bulk writes, DbContext lifetime or overlapping operations, search semantics, paging, raw SQL, and model/index coverage. Use whether or not EF Core owns the schema. For EF Core, not Dapper or raw ADO.NET."
license: MIT
---

# Optimizing EF Core Queries

Diagnose and fix slow or incorrect application data access built on EF Core. In a source-only review, trace the code without executing it and separate proven code behavior from runtime assumptions. When SQL, logs, or plans are available, use them to confirm the bottleneck. Prefer changes that reduce round-trips, duplicated rows, scans, or per-call translation cost over micro-optimizations.

## When to Use

- EF Core queries are slow or emit far more SQL statements than expected
- The same query repeats once per row (N+1 / lazy loading)
- Multiple collection `Include`s blow up or duplicate rows
- Deep pages slow down as `Skip` grows, or bulk updates load rows just to modify them
- A filtered/sorted query scans **even though the column is indexed**, or a filtered/sorted column has no supporting index
- A hot, frequently-executed query pays EF Core's LINQ-translation cost on every call

## When Not to Use

- **The code uses Dapper or raw ADO.NET, not EF Core.** Answer the SQL/indexing/query-plan question directly; do not introduce a `DbContext` or recommend `AsNoTracking`, `Include`, `AsSplitQuery`, or other EF Core APIs.

## Source review workflow

1. Find the EF boundary: `DbContext`, `DbSet`, repository implementation, unit of work, query helper, or model-building code. Follow the implementation rather than assuming what an interface method does.
2. Trace each query to execution or enumeration. Check whether operators bind to `Queryable` or `Enumerable`, including inside helpers; an `IEnumerable` can remain a deferred database sequence.
3. Count database operations and actual transaction boundaries along the calling path, including immediate writes and repeated enumeration. Also track which operations share the same `DbContext` instance
4. For every execution path, independently inspect predicates, expression construction, projection contents, result bounds, ordering, Includes, tracking, SQL parameterization, and model-building code.
5. Preserve the contract: result membership and order, random-page access, generated keys, retries, transaction and partial-failure behavior, concurrency handling, and domain/interceptor side effects.
6. Report prioritized findings with locations, the affected call path, concrete impact, and a safe remedy. Briefly identify sound counterparts and state what was not validated. Do not equate source reasoning with measured plans or speedups.

For a source-only request, recommend runtime validation as a next step.

For raw SQL, prefer EF Core's interpolated overloads (`FromSql` / `FromSqlInterpolated` and `ExecuteSql[Async]`) so values are sent as parameters.

## Gate source-only findings on evidence

When the user requests a source-only review, distinguish a demonstrated defect from an optimization that still needs workload or deployment evidence:

- A load-mutate loop followed by one `SaveChanges[Async]` is not `SaveChanges` in a loop. Do not call it a per-row round-trip defect; suggest a bulk API only when the operation is demonstrably large and bypassing tracking semantics is safe.
- Missing `HasIndex` calls in one supplied file do not prove that the deployed database lacks indexes. Use relevant model-building code, migrations, or a query plan as evidence. Do not pad a source review with generic conditional index advice when that evidence is absent.
- Missing `AsNoTracking` is actionable only on proven read-only entity results. Scalar-only projections have nothing to track, but an entity inside a DTO/record is still tracked by default.
- If every reviewed pattern is sound, say that no actionable changes are needed rather than manufacturing a finding.

## Capture SQL and query counts

Use supplied logs and plans first. For an executable investigation, enable command logging to verify query shape and count before and after a change:

```csharp
optionsBuilder.LogTo(Console.WriteLine, LogLevel.Information);
// or set "Microsoft.EntityFrameworkCore.Database.Command": "Information" in appsettings.json
```

Tag a query with `.TagWith("...")` to find it in the log. Count how many statements a slow operation runs, and how many rows each returns, before and after each change.

## Fixes

### Trace materialization and result bounds

Materialization inside a helper is still an execution boundary. A list or array has already loaded rows; later reductions run in memory. An `IEnumerable<T>` can instead hide a deferred EF query: its `Enumerable` operators process rows locally, and each enumeration may execute the database query again. Trace implementation and static types, not just method names. Keep translatable filters, projections, and aggregates on `IQueryable<T>` until execution:

```csharp
// Loads rows, then filters in memory.
var candidates = await repository.LoadCandidatesAsync(cancellationToken);
return candidates.Where(o => o.CreatedAt < cutoff).ToList();

// Composes and filters in SQL.
return await repository.QueryCandidates()
    .Where(o => o.CreatedAt < cutoff)
    .ToListAsync(cancellationToken);
```

Use `AnyAsync` for boolean existence, but preserve `CountAsync` when the numeric total is required. Combine repeated full-sequence summary calculations into server-side aggregates rather than caching an unbounded list or parallelizing queries on one context.

Separately review result cardinality. Page or cap interactive results; for complete exports, reduce buffering with streaming/chunks without truncating the contract.

### Keep predicates compatible with indexes

Functions/arithmetic on a column, or unanchored substring matching, commonly prevent a seek on an ordinary column index. Prefer a bare-column comparison, often a half-open range. Do not assert a full-table scan from LINQ alone: provider translations, computed/expression indexes, and other predicates can change the plan.

```csharp
// Computes a year expression instead of comparing the indexed date.
db.Logs.Where(l => l.CreatedAt.Year == year);

// Range compatible with an ordinary date index.
var start = new DateTime(year, 1, 1);
db.Logs.Where(l => l.CreatedAt >= start && l.CreatedAt < start.AddYears(1));
```

The same rule covers several common shapes:

- **Case-insensitive text** — use an existing indexed normalized/computed value or a compatible column collation instead of wrapping the column. Query-specific collation changes can also defeat an index with a different collation. Preserve normalization and comparison semantics. `StringComparison` overloads are generally not translated by relational providers; current EF Core throws for an untranslatable predicate rather than silently filtering locally.
- **Computed expressions** — compare against the precomputed constant, not `column * k > x`.
- **Converting the column to another type** — a predicate over `column.ToString()` (for example matching the *text form* of a number or date, `total.ToString().StartsWith(p)`) applies a function to every row and may not translate. Current EF Core reports a translation failure rather than silently evaluating an untranslatable predicate on the client. Filter on the typed column with a real comparison or range instead.
- **Substring search** — `name.Contains(term)` is generally not seekable with an ordinary B-tree index; prefix matching may be. Neither prefix matching nor token-based full-text search is a drop-in replacement for arbitrary substring matching. Preserve the required search mode or explicitly disclose the change; consider a provider-appropriate substring index/search design.

**Verify:** when runtime validation is requested, inspect the resulting predicate and plan. An additional ordinary index does not by itself repair an incompatible expression.

### Compile hot, frequently-executed queries

EF Core already caches translation for matching query shapes. On a very hot, otherwise minimal query, expression processing and cache lookup can still matter. `EF.CompileQuery` / `EF.CompileAsyncQuery` reuse a delegate that bypasses that lookup; they do not make database I/O cheaper:

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

### Control SQL parameterization and database plan reuse

Distinguish EF's query-translation cache from the database's execution-plan cache. Captured LINQ values normally become SQL parameters, while fixed literals remain constants. Parameters keep SQL text stable across values and usually promote database plan reuse; literals expose specific values to the optimizer and can improve selectivity estimates, but many distinct literal values can fragment the server plan cache. Keep the defaults unless workload evidence supports an override:

| Override | SQL effect | Performance decision |
|----------|------------|----------------------|
| `EF.Parameter(value)` (EF Core 9+) | Forces a parameter, including for a literal | Use when reusable SQL across values matters; already captured arguments usually need no override |
| `EF.Constant(value)` (EF Core 8.0.2+) | Forces an inline constant, including for a captured value | Reserve for evidenced value-specific plan benefits that outweigh extra SQL variants |

```csharp
db.Orders.Where(order => order.Status == EF.Parameter("Pending"));
db.Orders.Where(order => order.CustomerId == EF.Constant(customerId));
```

The first query parameterizes a fixed status; the second specializes SQL for each customer ID. Neither is inherently faster. Do not parameterize every structural literal, or inline high-cardinality request values without evidence. Provider behavior, parameter-sensitive plans, data skew, and collection translations affect the tradeoff.

In EF Core 9, these overrides are processed later than query-shape caching: `EF.Constant` can reuse EF's translation while still generating different SQL literals. It is not equivalent to embedding each request value with `Expression.Constant` in a dynamic tree. Compiled delegates do not fix server plan-cache fragmentation; EF Core 9 also does not support these overrides inside `EF.CompileQuery` / `EF.CompileAsyncQuery`.

**Verify:** compare generated SQL, server compilation/plan reuse, estimates, and representative latency when execution is available. A source review can identify the override and likely tradeoff, not prove a faster plan.

### Stabilize dynamically constructed query shapes

Follow request values into expression builders. `Expression.Constant(requestValue)` inside a comparison can produce a new EF cache entry and different SQL for each value. Represent changing values through correctly typed captured-value expressions instead:

```csharp
Expression<Func<string>> valueAccess = () => filterValue;
var comparison = Expression.Equal(propertyExpression, valueAccess.Body);
```

Creating the entity's `ParameterExpression` does not parameterize its comparison values. Ordinary captured LINQ arguments are already parameterizable; fixed structural constants are not automatically defects. Preserve null/conversion semantics, and canonicalize equivalent filter order only when it is safe. Making the builder static, caching a delegate per value, or compiling every new dynamic expression does not stabilize the shape. Report potential compilation/cache churn, not a proven unbounded leak.

### Choose tracking for the actual result graph

| Result and consumer | Decision |
|---------------------|----------|
| Scalar/value-only projection | No entity tracking to remove; do not add `AsNoTracking` as a finding |
| Entity or entity-valued DTO used for later edits | Preserve tracking and its identity semantics |
| Read-only entity graph | Consider no tracking; narrow the projection first if full entities are unnecessary |
| Read-only graph with heavily shared entities or reference-identity consumers | Consider `AsNoTrackingWithIdentityResolution`, or key-based DTO processing |

Plain `AsNoTracking` does **not** resolve repeated keys to one object. Identity-resolution mode uses a query-local map without attaching results to the context; it costs bookkeeping and does not reduce joined SQL payload or deduplicate across separate queries.

For filtered Includes, inspect earlier/later loads on the same tracking context. Navigation fixup can add already tracked children that fail the Include predicate; the filtered navigation is also marked loaded. Use a detached projection with its own child filter, an appropriate no-tracking read, or a fresh read context when the subset matters. Do not clear a write tracker with pending work just to clean up a read.

### Remove N+1 and avoid lazy loading

The same `SELECT` repeated once per row (a navigation accessed inside a loop) is an N+1. Load the related data in one round-trip — project the aggregates with `Select`, or eager-load with `Include`:

```csharp
var summaries = await db.Orders
    .Select(o => new OrderSummary(o.Id, o.Items.Count, o.Items.Sum(i => i.Price)))
    .ToListAsync();
```

Prefer projection or `Include` over lazy loading: lazy loading is a leading cause of N+1 and forces synchronous I/O. In server apps, don't enable `Microsoft.EntityFrameworkCore.Proxies` or mark navigations `virtual` for lazy loading.

**Verify:** a fixed, small query count regardless of row count.

### Separate join multiplication from wide-row duplication

Sibling collection Includes can multiply each other's rows; a nested collection chain is not the same cross-product, and references do not count as additional collections. Even one collection can repeat a wide parent blob/text column many times. Prefer a narrow projection when the consumer does not need that column or graph. Otherwise consider `AsSplitQuery` or targeted loading:

```csharp
db.Blogs.Include(b => b.Posts).Include(b => b.Contributors).AsSplitQuery();
```

Splitting adds statements/round-trips and can buffer results. Preserve deliberate `AsSingleQuery` and consistency requirements; stable multi-statement reads may need an appropriate snapshot/serializable transaction. Paged results need unique ordering (especially split pagination before EF Core 10); ordering is not what establishes entity identity. Do not prescribe splitting merely by counting Includes.

**Verify:** lower payload/row duplication outweighs extra round-trips with the required consistency.

### Filter and paginate; prefer keyset over offset

Constrain large result sets with `Where`, and page with **keyset (seek)** pagination rather than `Skip`/`Take`, which still scans and discards the skipped rows on deep pages:

```csharp
db.Orders.Where(o => o.Id > lastSeenId).OrderBy(o => o.Id).Take(pageSize);
```

Order by a unique, stable key with suitable index coverage. A descending `(CreatedAt, Id)` cursor requires `CreatedAt < lastDate || (CreatedAt == lastDate && Id < lastId)`, not independent bounds on both fields. Include every ordering key with its direction; IDs need not follow business timestamps.

Trace ordering through query helpers and execution boundaries:

- A second `OrderBy`/`OrderByDescending` on an `IQueryable` replaces the earlier primary ordering; use `ThenBy` for a secondary key.
- `OrderBy(...).Skip(...).Take(...).AsEnumerable().OrderBy(...)` first chooses a stable database window and then reorders only that bounded window in memory. That can be intentional and is not an unordered page.
- `Skip`/`Take` without a stable unique order is nondeterministic. Deep offset paging is a separate scaling concern, and keyset pagination is not behavior-preserving for random page-number access.
- Even fully ordered offset pages can shift under concurrent inserts/deletes; adding an order fixes nondeterminism, not cross-request snapshot consistency.

**Verify:** page latency stays roughly constant from early to deep pages.

### Add missing indexes

Audit workload-specific coverage separately from moving filters into SQL. Inspect applied model configurations and conventions. Derive each dependent FK's ordered properties from `HasForeignKey`; EF normally supplies an index on that tuple unless an existing index/key covers the ordered prefix and required uniqueness, or the convention was removed. Do not count only `HasIndex` calls or confuse a principal key with the dependent table's coverage. Status, timestamps, and other non-key columns may need explicit coverage. Recommend evidenced gaps for hot queries, not generic indexes in every review.

If EF Core owns the schema, add the index in the model and migrate:

```csharp
modelBuilder.Entity<Order>()
    .HasIndex(o => new { o.CustomerId, o.CreatedAt }); // equality column first, then range/sort
```

Then create the migration with `dotnet ef migrations add ...`. **Do not apply it** with `dotnet ef database update` (or any equivalent that writes to the database) without explicit user approval — applying a migration mutates the database, so add the migration, show it to the user, and let them run the update once they've reviewed it. If EF Core does not own the schema, recommend the same index to whoever manages the database. Don't over-index — every index slows writes.

Match equality columns, then range/sort keys. An unconstrained intermediate key can block the desired range/order even if an earlier equality column is covered. When all initial keys use equality, more than one order may work; do not condemn a reversed composite index from relationship metadata alone. A FK index can cover the relationship but not a different date-ordered workload. Consider an additional workload index rather than replacing one that supports another path. Model code proves intended metadata, not current physical indexes; weigh write cost and provider-specific coverage.

**Verify:** the plan uses a seek/index instead of a scan.

### Trace persistence through repositories

A service loop can hide `SaveChanges` behind a repository, audit journal, or progress hook. One final unit-of-work commit does not undo earlier writes; establish whether an enclosing transaction exists before claiming per-item commits. A save can itself require multiple SQL batches.

Stage bounded batches and save together when the contract permits. Preserve audit/outbox records, generated keys, relationship fixup, ordering, concurrency, and domain events. Capture IDs from the input-ordered entity references after saving; do not assume numerical ID allocation order.

For retried feeds, reason about what already committed and what the caller can replay. Preserve or design durable row/feed identity with uniqueness and replay handling, or acknowledged checkpoints. Chunking alone is not idempotency, and an unprotected existence-check-then-insert is not race-safe.

### Bound change detection in growing imports

Successfully saved entities remain tracked. With automatic detection enabled, `ChangeTracker.Entries<T>()`, `HasChanges`, and `SaveChanges` inspect tracked state; filtering entries by type does not limit the preceding scan. Repeated per-row inspection of a growing graph can create superlinear CPU work even when saves are chunked.

Use import-owned progress counters instead of repeated tracker enumeration, and rotate a context or clear completed work only when that context is owned by the batch. Preserve pending changes and references. EF Core `Add` and `AddRange` do **not** automatically call `DetectChanges`; replacing one with the other is not a detection fix. Suppress automatic detection only for a demonstrated redundant scan, restore its previous setting in `finally`, and explicitly detect mutations when required before persistence/auditing.

### Set-based bulk updates and deletes

For demonstrably large, simple updates/deletes that need no per-entity processing, consider `ExecuteUpdateAsync`/`ExecuteDeleteAsync` (EF Core 7+ and a supporting provider). They execute immediately without materialization:

```csharp
await db.Products.Where(p => p.LastSoldDate < cutoff)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false));
```

Check the entire write workflow:

- Immediate writes do not update loaded entity values or snapshots. Later mutations of the same properties can overwrite the SQL change **even in one sequential request**. Use one coherent tracked save or an explicitly synchronized/untracked set-based path; a transaction alone does not refresh tracked state.
- Configured concurrency tokens are not automatically checked by these APIs. Preserve token predicates, token updates, and affected-row handling when optimistic concurrency is required.
- Each call executes separately; a later `SaveChanges` neither batches nor makes them atomic with audit/outbox writes. Use one save unit or an explicit shared transaction where required.
- Preserve client-side cascades and SaveChanges-based hooks. Command interceptors are different and can still observe the SQL. Do not bypass domain behavior merely to reduce query count.

**Verify:** a single `UPDATE`/`DELETE` with a `WHERE` and no preceding `SELECT`.

### Keep DbContext operations non-overlapping

EF Core does not support overlapping operations on one `DbContext`, including through distinct repository objects constructed with that context. Starting both calls before awaiting either is enough; `Task.WhenAll` does not make sharing safe. Await sequentially, or create/dispose one independent context per concurrent operation. Separate factory-created contexts are safe to overlap. A static or singleton-held context also creates lifetime/thread-safety hazards.

## Common Pitfalls

| Pitfall | Fix |
|---------|-----|
| Function-wrapped predicate incompatible with an ordinary index | Use a range or an existing indexed normalized/computed value |
| Fixing query composition but ignoring workload index coverage | Match the actual equality/range/order prefix, including convention-created indexes |
| Compiling a query that runs only occasionally | Compile only genuinely hot, high-frequency query shapes |
| Confusing EF translation reuse with database plan reuse | Inspect SQL parameter/literal choices and justify overrides with workload evidence |
| DTO contains entities but is assumed untracked | Inspect the materialized graph, not the outer return type |
| Bulk write followed by tracked arithmetic on stale values | Unify writes and preserve concurrency/atomicity; do not just add a transaction |
| Repeated enumeration of a repository's deferred `IEnumerable` | Compose database aggregates before crossing to local operators |

## References

- [Efficient querying — EF Core](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying)
- [Compiled queries — EF Core](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#compiled-queries)
- [Force or prevent query parameterization — EF Core 9](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/whatsnew#force-or-prevent-query-parameterization)
- [Parameterization overrides inside compiled queries — EF Core 9](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/breaking-changes)
- [Efficient updating (ExecuteUpdate/ExecuteDelete) — EF Core](https://learn.microsoft.com/en-us/ef/core/performance/efficient-updating)
- [Single vs. split queries](https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries)
- [Pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination)
- [Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)
