# Entity Framework Core Breaking Changes (.NET 11)

These breaking changes affect projects using Entity Framework Core 11. Source: https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-11.0/breaking-changes

> **Note:** .NET 11 is in preview. The changes below were introduced in **Preview 1**. Additional EF Core breaking changes are expected in later previews.

## Medium-Impact Changes

### Sync I/O via the Azure Cosmos DB provider has been fully removed (Preview 1)

**Impact: Medium.** Synchronous I/O via the Azure Cosmos DB provider has been completely removed. In EF Core 10, sync I/O was unsupported by default but could be re-enabled with a special opt-in. In EF Core 11, calling any synchronous I/O API always throws — there is no opt-in to restore the old behavior.

**Affected APIs:**
- `ToList()`, `First()`, `Single()`, `Count()`, and other synchronous LINQ operators
- `SaveChanges()`
- Any synchronous query execution against the Cosmos DB provider

```csharp
// BREAKS in EF Core 11 — always throws
var items = context.Items.ToList();
context.SaveChanges();

// FIX: Use async equivalents
var items = await context.Items.ToListAsync();
await context.SaveChangesAsync();
```

**Why:** Synchronous blocking on asynchronous methods ("sync-over-async") can lead to deadlocks and performance problems. Since the Azure Cosmos DB SDK only supports async methods, the EF Cosmos provider now requires async throughout.

**Fix:** Convert all synchronous I/O calls to their async equivalents:
- `ToList()` → `await ToListAsync()`
- `First()` → `await FirstAsync()`
- `Single()` → `await SingleAsync()`
- `Count()` → `await CountAsync()`
- `SaveChanges()` → `await SaveChangesAsync()`
- `Any()` → `await AnyAsync()`

Tracking issue: https://github.com/dotnet/efcore/issues/37059
