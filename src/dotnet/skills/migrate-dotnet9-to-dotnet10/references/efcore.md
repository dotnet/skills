# Entity Framework Core 10 Breaking Changes

These changes affect projects using EF Core or Microsoft.Data.Sqlite.

## Medium-Impact Changes

### EF tools require framework to be specified for multi-targeted projects

When running EF tools on a project with `<TargetFrameworks>` (plural), you must now specify `--framework`:

```bash
dotnet ef migrations add MyMigration --framework net10.0
dotnet ef database update --framework net10.0
```

Without this, you'll get: "The project targets multiple frameworks. Use the --framework option to specify which target framework to use."

## Low-Impact Changes

### Application Name injected into connection string

EF now inserts an `Application Name` containing EF and SqlClient version info into connection strings that don't already have one. This changes the effective connection string, which can cause:
- Separate connection pools when mixing EF and non-EF data access (e.g., Dapper)
- Potential distributed transaction escalation within `TransactionScope`

**Mitigation:** Explicitly set `Application Name` in your connection string.

### SQL Server json data type used by default on Azure SQL and compatibility level 170

For Azure SQL (`UseAzureSql`) or compatibility level ≥170, EF now maps JSON columns to the `json` data type instead of `nvarchar(max)`. A migration will be generated to alter existing columns.

**Considerations:**
- SQL Server does not support `DISTINCT` over JSON arrays — queries using it will fail
- The column alteration is a non-trivial schema change

**Mitigation:**
```csharp
// Option 1: Set compatibility level below 170
optionsBuilder.UseAzureSql(connStr, o => o.UseCompatibilityLevel(160));

// Option 2: Explicitly set column type per property
modelBuilder.Entity<Blog>()
    .PrimitiveCollection(b => b.Tags)
    .HasColumnType("nvarchar(max)");
```

### Parameterized collections now use multiple parameters by default

`.Contains()` on collections now translates to `WHERE x IN (@p1, @p2, @p3)` instead of using `OPENJSON`. This gives the query planner cardinality information but may regress performance for large collections.

**Mitigation:**
```csharp
// Global: revert to JSON parameter mode
optionsBuilder.UseSqlServer(connStr,
    o => o.UseParameterizedCollectionMode(ParameterTranslationMode.Parameter));

// Per-query: use EF.Parameter() for JSON array translation
var blogs = await context.Blogs
    .Where(b => EF.Parameter(ids).Contains(b.Id))
    .ToListAsync();
```

### ExecuteUpdateAsync now accepts a regular lambda

`ExecuteUpdateAsync` now takes `Func<...>` instead of `Expression<Func<...>>` for column setters. Code that manually builds expression trees for dynamic setters will no longer compile but can be dramatically simplified:

```csharp
// Before (.NET 9) — complex expression tree construction
Expression<Func<SetPropertyCalls<Blog>, SetPropertyCalls<Blog>>> setters =
    s => s.SetProperty(b => b.Views, 8);
// ... manual Expression.Lambda, Expression.Call, etc.

// After (.NET 10) — simple lambda with conditionals
await context.Blogs.ExecuteUpdateAsync(s =>
{
    s.SetProperty(b => b.Views, 8);
    if (nameChanged)
        s.SetProperty(b => b.Name, "foo");
});
```

### Complex type column names are now uniquified

If multiple complex types have properties with the same name, column names are now uniquified by appending a number. This may generate a migration that renames columns.

**Mitigation:** Explicitly configure column names with `HasColumnName()`.

### Nested complex type properties use full path in column names

`EntityType.Complex.NestedComplex.Property` is now mapped to `Complex_NestedComplex_Property` (was `NestedComplex_Property`). This generates a migration renaming columns.

**Mitigation:** Use `HasColumnName()` to preserve old names.

### IDiscriminatorPropertySetConvention signature changed

The method parameter changed from `IConventionEntityTypeBuilder` to `IConventionTypeBaseBuilder`. Update custom convention implementations.

### IRelationalCommandDiagnosticsLogger methods add logCommandText parameter

Methods like `CommandReaderExecuting`, `CommandReaderExecuted`, etc. now have an additional `string logCommandText` parameter containing redacted SQL for logging. Update custom implementations.

## Microsoft.Data.Sqlite Breaking Changes (All High Impact)

### GetDateTimeOffset without an offset now assumes UTC

Previously, a textual timestamp without an offset (e.g., `2014-04-15 10:47:16`) was parsed using the local timezone. Now it's treated as UTC.

### Writing DateTimeOffset into REAL column now writes in UTC

`DateTimeOffset` values written to REAL columns are now converted to UTC before writing. Previously the offset was ignored.

### GetDateTime with an offset now returns value in UTC

`GetDateTime` on timestamps with offsets (e.g., `2014-04-15 10:47:16+02:00`) now returns the value converted to UTC with `DateTimeKind.Utc`. Previously it returned `DateTimeKind.Local`.

**Mitigation for all three:**
```csharp
// Temporary workaround to revert to .NET 9 behavior
AppContext.SetSwitch("Microsoft.Data.Sqlite.Pre10TimeZoneHandling", true);
```

Review all date/time handling code that reads from or writes to SQLite databases. The new behavior aligns with SQLite's convention that timestamps are UTC.
