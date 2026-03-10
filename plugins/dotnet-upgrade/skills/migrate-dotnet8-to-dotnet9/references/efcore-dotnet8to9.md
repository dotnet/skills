# Entity Framework Core 9 Breaking Changes

These changes affect projects using EF Core.

## High-Impact Changes

### Exception is thrown when applying migrations if there are pending model changes

**Impact: High.** `dotnet ef database update`, `Migrate()`, and `MigrateAsync()` now throw if the model has pending changes compared to the last migration:

> The model for context 'DbContext' has pending changes. Add a new migration before updating the database.

**Common causes:**
- No migrations exist at all (database managed through other means)
- Model snapshot is missing (migration was created manually)
- Non-deterministic model building (`DateTime.Now`, `Guid.NewGuid()` in `HasData`)
- Last migration created for a different provider
- ASP.NET Core Identity options that affect the model aren't applied in design-time factory

**Non-deterministic `HasData` example (common pitfall):**
```csharp
// BREAKS — DateTime.UtcNow changes every evaluation, causing "pending model changes"
modelBuilder.Entity<Order>().HasData(
    new Order { Id = 1, CreatedAt = DateTime.UtcNow });

// Fix — use a fixed constant
modelBuilder.Entity<Order>().HasData(
    new Order { Id = 1, CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
```

**Mitigation (temporary workaround — not recommended for production):**
```csharp
// Suppress the warning if intentional — review before deploying to production,
// as this risks silent schema drift between the model and the database.
options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
```

### Exception is thrown when applying migrations in an explicit transaction

**Impact: High.** `Migrate()` and `MigrateAsync()` now manage their own transactions and `ExecutionStrategy`. Wrapping them in a user transaction throws:

> A transaction was started before applying migrations. This prevents a database lock to be acquired.

```csharp
// BREAKS
await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
{
    await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
    await dbContext.Database.MigrateAsync(ct);
    await transaction.CommitAsync(ct);
});

// Fix — remove the external transaction
await dbContext.Database.MigrateAsync(ct);
```

**Mitigation (temporary workaround):** If you need the explicit transaction:
```csharp
// Suppress if you understand the transaction safety implications.
// EF Core manages its own transactions during migration; wrapping in a user
// transaction can prevent proper lock acquisition.
options.ConfigureWarnings(w => w.Ignore(RelationalEventId.MigrationsUserTransactionWarning));
```

## Medium-Impact Changes

### `Microsoft.EntityFrameworkCore.Design` not found when using EF tools

**Impact: Medium.** Starting with .NET SDK 9.0.200, the EF tools may fail with: `Could not load file or assembly 'Microsoft.EntityFrameworkCore.Design'`. This is caused by a change in how private assets are included in `.deps.json`.

**Mitigation:** Mark the Design package as publishable:
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.1">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  <Publish>true</Publish>
</PackageReference>
```

## Low-Impact Changes

### `EF.Functions.Unhex()` now returns `byte[]?`

The return type annotation changed from `byte[]` to `byte[]?` to match SQLite's `unhex` function which returns NULL for invalid inputs. Add the null-forgiving operator (`!`) if you're certain the input is valid.

### Compiled models reference value converter methods directly

Value converter methods referenced by compiled models must now be `public` or `internal` (not `private`), as the generated code references them directly for NativeAOT support.

### SqlFunctionExpression nullability arguments arity validated

The number of arguments and nullability propagation arguments must now match. When in doubt, use `false` for the nullability argument.

### `ToString()` returns empty string for null instances

`ToString()` on nullable value types in EF queries now consistently returns empty string when the value is `null`, matching C# behavior. Previously the result was inconsistent across types.

### Shared framework dependencies updated to 9.0.x

EF Core 9.0 references 9.0.x versions of `System.Text.Json`, `Microsoft.Extensions.Caching.Memory`, etc. Apps targeting `net8.0` will deploy these assemblies separately instead of using the shared framework.

## Azure Cosmos DB Breaking Changes

EF Core 9.0 has extensive Cosmos DB changes. If using the Cosmos DB provider:

### Discriminator property renamed to `$type` (High Impact)

The default discriminator property name changed from `Discriminator` to `$type` to align with `System.Text.Json` conventions. Existing documents use the old name.

**Mitigation:**
```csharp
modelBuilder.Entity<Session>().HasDiscriminator<string>("Discriminator");
```

### `id` property no longer contains discriminator (High Impact)

The `id` property now contains only the EF key property value (e.g., `123`), not `EntityType|KeyValue` (e.g., `Product|123`). Existing documents written by EF Core 8 still have the old compound format.

**Implications for existing data:**
- `FindAsync` and point-reads by id will fail because EF Core 9 generates `123` but the stored document has `Product|123`
- New documents get the short format, creating id inconsistency with existing documents
- Direct Cosmos SQL queries matching on `c.id` need updating

**Mitigation:** Preserve the old id format so existing documents remain accessible:
```csharp
modelBuilder.Entity<Product>().HasRootDiscriminatorInJsonId(true);
```

### JSON `id` property mapped to key (High Impact)

The JSON `id` property is now mapped directly to the entity key property.

### Sync I/O no longer supported (Medium Impact)

Synchronous methods like `SaveChanges()` and `ToList()` on the Cosmos DB provider now throw. Use async equivalents.

### SQL queries must project JSON values directly (Medium Impact)

Raw SQL queries against the Cosmos DB provider must now project values from the JSON document directly using `VALUE`.

### Undefined results filtered from query results (Medium Impact)

Query results that are `undefined` in Cosmos DB are now automatically filtered out.

### Incorrectly translated queries no longer translated (Medium Impact)

Some queries that were previously translated incorrectly now throw `InvalidOperationException` to prevent silent data corruption.

### `HasIndex` now throws (Medium Impact)

`HasIndex` on a Cosmos DB entity type now throws `InvalidOperationException` at startup instead of being silently ignored. Remove all `HasIndex` calls for Cosmos entities — Cosmos DB indexing is managed through the container's indexing policy (Azure Portal, Bicep, or Terraform), not through EF Core.

### `IncludeRootDiscriminatorInJsonId` renamed (Low Impact)

Renamed to `HasRootDiscriminatorInJsonId` after RC2.
