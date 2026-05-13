# Entity Framework Core Breaking Changes (.NET 11)

These breaking changes affect projects using Entity Framework Core 11. Source: https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-11.0/breaking-changes

> **Note:** .NET 11 is in preview. The changes below were introduced in **Preview 1 through Preview 3**. Additional EF Core breaking changes are expected in later previews.

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

### Cosmos: empty owned collections return empty collection instead of null (Preview 1)

**Impact: Low.** When a Cosmos-backed entity has an owned collection with no items, the property now returns an empty collection rather than `null`.

**Fix:** Update null checks to empty-collection checks: `if (entity.Items is null)` → `if (entity.Items.Count == 0)`.

Tracking issue: https://github.com/dotnet/efcore/issues/36577

## Preview 3 Changes

### RelationalEventId.MigrationsNotFound now throws by default (Preview 3)

**Impact: Low.** Calling `Migrate()` or `MigrateAsync()` when no migrations exist in the assembly now throws an exception rather than silently logging.

**Fix:** If intentional, suppress with: `options.ConfigureWarnings(w => w.Ignore(RelationalEventId.MigrationsNotFound))`.

Source: https://github.com/dotnet/efcore/pull/37839

### EF Core Tools and Tasks no longer transitively depend on Design (Preview 3)

**Impact: Low.** The `Microsoft.EntityFrameworkCore.Tools` and `Microsoft.EntityFrameworkCore.Tasks` NuGet packages no longer have a transitive dependency on `Microsoft.EntityFrameworkCore.Design`.

**Fix:** If your project relied on this transitive reference, add it explicitly:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="11.0.0" PrivateAssets="all" />
```

Source: https://github.com/dotnet/efcore/pull/37837

### EFOptimizeContext MSBuild property removed (Preview 3)

**Impact: Low.** The `<EFOptimizeContext>true</EFOptimizeContext>` MSBuild property no longer exists. Code generation is now controlled by `<EFScaffoldModelStage>` and `<EFPrecompileQueriesStage>`.

**Fix:** Replace `<EFOptimizeContext>` with the two new properties. With `PublishAOT=true`, generation is automatic during publish.

Source: https://github.com/dotnet/efcore/pull/37838

### SqlVector&lt;T&gt; properties excluded from SELECT by default (Preview 3)

**Impact: Low.** `SqlVector<T>` properties are now excluded from `SELECT` statements when materializing entities (they return `null`). They can still be used in `WHERE`/`ORDER BY` for vector search.

**Fix:** Use explicit projections to include vector values: `.Select(b => new { b.Id, b.Embedding })`.

Source: https://github.com/dotnet/efcore/pull/37829

### Microsoft.Data.SqlClient updated to 7.0 (Preview 3)

**Impact: Medium.** EF Core's SQL Server provider now depends on `Microsoft.Data.SqlClient` 7.0. In v7, Azure/Entra ID authentication dependencies (`Azure.Core`, `Azure.Identity`, `Microsoft.Identity.Client`) have been removed from the core package.

**Fix:** If using Entra ID authentication (e.g., `ActiveDirectoryDefault`, `ActiveDirectoryManagedIdentity`), add:

```xml
<PackageReference Include="Microsoft.Data.SqlClient.Extensions.Azure" Version="7.0.0" />
```

Source: https://github.com/dotnet/efcore/pull/37949

### Encryption-enabled SQLite packages removed (Preview 3)

**Impact: Medium.** `SQLitePCLRaw 3.0` (used by `Microsoft.Data.Sqlite` 11) removed `bundle_e_sqlcipher` and several other bundle packages.

**Fix:** Switch to SQLite Encryption Extension (SEE), SQLCipher from Zetetic, or `SQLite3MultipleCiphers-NuGet`.

Source: https://github.com/dotnet/efcore/issues/37059
