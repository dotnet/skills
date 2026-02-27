---
name: optimizing-ef-core-queries
description: Optimize Entity Framework Core queries by fixing N+1 problems, choosing correct tracking modes, using compiled queries, and avoiding common performance traps. Use when EF Core queries are slow, generating excessive SQL, or causing high database load.
---

# Optimizing EF Core Queries

## When to Use

- EF Core queries are slow or generating too many SQL statements
- Database CPU/IO is high due to ORM inefficiency
- N+1 query patterns are detected in logs
- Large result sets cause memory pressure

## When Not to Use

- The user is using Dapper or raw ADO.NET (not EF Core)
- The performance issue is database-side (missing indexes, bad schema)
- The user is building a new data access layer from scratch

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Slow EF Core queries | Yes | The LINQ queries or DbContext usage to optimize |
| SQL output or logs | No | EF Core generated SQL or query execution logs |

## Workflow

### Step 1: Enable query logging to see the actual SQL

```csharp
// In Program.cs or DbContext configuration:
optionsBuilder
    .UseSqlServer(connectionString)
    .LogTo(Console.WriteLine, LogLevel.Information)
    .EnableSensitiveDataLogging()  // shows parameter values (dev only!)
    .EnableDetailedErrors();
```

Or use the `Microsoft.EntityFrameworkCore` log category:

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  }
}
```

### Step 2: Fix N+1 query patterns

**The #1 EF Core performance killer.** Happens when loading related entities in a loop.

**Before (N+1 — 1 query for orders + N queries for items):**
```csharp
var orders = await db.Orders.ToListAsync();
foreach (var order in orders)
{
    // Each access triggers a lazy-load query!
    var items = order.Items.Count;
}
```

**After (eager loading — 1 or 2 queries total):**
```csharp
// Option 1: Include (JOIN)
var orders = await db.Orders
    .Include(o => o.Items)
    .ToListAsync();

// Option 2: Split query (separate SQL, avoids cartesian explosion)
var orders = await db.Orders
    .Include(o => o.Items)
    .AsSplitQuery()
    .ToListAsync();

// Option 3: Explicit projection (best - only fetches needed columns)
var orderSummaries = await db.Orders
    .Select(o => new OrderSummary
    {
        OrderId = o.Id,
        Total = o.Items.Sum(i => i.Price),
        ItemCount = o.Items.Count
    })
    .ToListAsync();
```

**When to use Split vs Single query:**

| Scenario | Use |
|----------|-----|
| 1 level of Include | Single query (default) |
| Multiple Includes (Cartesian risk) | `AsSplitQuery()` |
| Include with large child collections | `AsSplitQuery()` |
| Need transaction consistency | Single query |

### Step 3: Use NoTracking for read-only queries

**Change tracking overhead is significant.** Disable it when you don't need to update entities:

```csharp
// Per-query
var products = await db.Products
    .AsNoTracking()
    .Where(p => p.IsActive)
    .ToListAsync();

// Global default for read-heavy apps
services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString)
           .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
```

**Use `AsNoTrackingWithIdentityResolution()` when the query returns duplicate entities to avoid duplicated objects in memory.**

### Step 4: Use compiled queries for hot paths

```csharp
// Define once as static
private static readonly Func<AppDbContext, int, Task<Order?>> GetOrderById =
    EF.CompileAsyncQuery((AppDbContext db, int id) =>
        db.Orders
            .Include(o => o.Items)
            .FirstOrDefault(o => o.Id == id));

// Use repeatedly — skips query compilation overhead
var order = await GetOrderById(db, orderId);
```

### Step 5: Avoid common query traps

| Trap | Problem | Fix |
|------|---------|-----|
| `ToList()` before `Where()` | Loads entire table into memory | Filter first: `.Where().ToList()` |
| `Count()` to check existence | Scans all rows | Use `.Any()` instead |
| `.Select()` after `.Include()` | Include is ignored with projection | Remove Include, use Select only |
| `string.Contains()` in Where | May not translate, falls to client eval | Use `EF.Functions.Like()` for SQL LIKE |
| Calling `.ToList()` inside `Select()` | Causes nested queries | Use projection with `Select` all the way |

### Step 6: Use raw SQL or FromSql for complex queries

When LINQ can't express it efficiently:

```csharp
var results = await db.Orders
    .FromSqlInterpolated($@"
        SELECT o.* FROM Orders o
        INNER JOIN (
            SELECT OrderId, SUM(Price) as Total
            FROM OrderItems
            GROUP BY OrderId
            HAVING SUM(Price) > {minTotal}
        ) t ON o.Id = t.OrderId")
    .AsNoTracking()
    .ToListAsync();
```

## Validation

- [ ] SQL logging shows expected number of queries (no N+1)
- [ ] Read-only queries use `AsNoTracking()`
- [ ] Hot-path queries use compiled queries
- [ ] No client-side evaluation warnings in logs
- [ ] Include/split strategy matches data shape

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Lazy loading silently creating N+1 | Remove `Microsoft.EntityFrameworkCore.Proxies` or disable lazy loading |
| Global query filters forgotten in perf analysis | Check `HasQueryFilter` in model config; use `IgnoreQueryFilters()` if needed |
| `DbContext` kept alive too long | DbContext should be scoped (per-request); don't cache it |
| Batch updates fetching then saving | EF Core 7+: use `ExecuteUpdateAsync` / `ExecuteDeleteAsync` for bulk operations |
| String interpolation in `FromSqlRaw` | SQL injection risk — use `FromSqlInterpolated` (parameterized) |
