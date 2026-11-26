# Entity Framework Core Breaking Changes (.NET 11)

These breaking changes affect projects using Entity Framework Core 11. Source: https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-11.0/breaking-changes

Several of the highest-impact changes compile cleanly and only show against existing data. Check the provider-specific sections for every provider the project uses.

## Azure Cosmos DB Provider

### Unmapped properties are no longer preserved

**Impact: High (data loss).** In EF Core 10, JSON properties of a Cosmos document that were not part of the EF model were kept in the `__jObject` shadow property and written back on `SaveChanges`. In EF Core 11 they are ignored when reading, so loading an entity and saving it again removes those properties from the stored document. Nothing fails at build or run time.

**Fix:**
- Find documents that carry properties the model does not map (fields written by other services, older schema versions, or properties removed from the entity class).
- Map every property that must survive in the EF model, or read and write those documents through `CosmosClient` directly (`context.Database.GetCosmosClient()`).
- Test a load-modify-save round trip against a copy of real data before deploying.

Source: https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-11.0/breaking-changes

### Illegal `id` characters are no longer escaped

**Impact: Medium.** When EF Core builds a composite `id` from key values, it no longer escapes `/`, `\`, `?`, and `#` (previously `^2F`, `^5C`, `^3F`, and `^23`). The `|` separator and any `|` in key values are still escaped. Documents saved by EF Core 10 with escaped ids, such as `Post|1|^2F`, are not found by the new unescaped id `Post|1|/`.

**Fix:** If existing documents may contain escaped ids, restore the old behavior:

```csharp
AppContext.SetSwitch("Microsoft.EntityFrameworkCore.EscapeIllegalCosmosIdCharacters", true);
```

The old scheme never escaped `^`, so a key containing the literal text `^2F` collides with a key containing `/`; the switch keeps that collision. For new databases, avoid these characters in key values.

Source: https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-11.0/breaking-changes

### Sync I/O has been fully removed

**Impact: Medium.** In EF Core 10, synchronous I/O with the Cosmos provider was unsupported by default but could be re-enabled. In EF Core 11, every synchronous I/O API always throws, and there is no opt-in.

```csharp
// Throws in EF Core 11
var items = context.Items.ToList();
context.SaveChanges();

// Fix: use the async equivalents
var items = await context.Items.ToListAsync();
await context.SaveChangesAsync();
```

**Fix:** Convert `ToList()`, `First()`, `Single()`, `Count()`, `Any()`, and `SaveChanges()` (and other synchronous query operators) to their async equivalents.

Tracking issue: https://github.com/dotnet/efcore/issues/37059

### Exception when a projection evaluates to undefined

**Impact: Medium.** A projection that evaluates to `undefined` in Cosmos now throws instead of returning a default value: "A part of the projection was undefined, use the coalesce operator to handle possible undefined values."

**Fix:** Filter with `EF.Functions.IsDefined(...)` in a `Where`, or project with `EF.Functions.CoalesceUndefined(..., default)`.

### Other Cosmos changes

**Impact: Low.**
- The `__jObject` shadow property is removed; accessing it throws `InvalidOperationException`. Read raw documents with `context.Database.GetCosmosClient()` and `ReadItemAsync<JsonElement>`.
- An owned collection with no items now returns an empty collection instead of `null`. Replace `is null` checks with `is { Count: 0 }`.
- The default discriminator property is named `Discriminator` in the model (the JSON name stays `$type`). Code that refers to the model property name `$type` must switch to `Discriminator`: queries and query filters such as `EF.Property<string>(e, "$type")`, and metadata lookups (`FindDiscriminatorProperty().Name` now returns `Discriminator`).
- Floating-point values materialized into fixed-point types are truncated instead of rounded (`0.75` becomes `0`).

## SQL Server Provider

### Compatibility level defaults to 160

**Impact: Low–Medium (fails only against older servers).** `UseSqlServer` without an explicit compatibility level now targets 160 (SQL Server 2022) instead of 150 (SQL Server 2019). Queries using `Math.Min`, `Math.Max`, and some `Take`/`Skip` patterns can now translate to `LEAST` and `GREATEST`, which fail on SQL Server 2019 or older, and on databases set to a compatibility level below 160. The application still builds and the failure appears only when the query runs.

**Fix:** Match the compatibility level of the oldest database the application runs against:

```csharp
optionsBuilder.UseSqlServer(connectionString, o => o.UseCompatibilityLevel(150));
```

Source: https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-11.0/breaking-changes#sql-server-compatibility-level-now-defaults-to-160

### Microsoft.Data.SqlClient updated to 7.0

**Impact: Medium.** The SQL Server provider depends on `Microsoft.Data.SqlClient` 7.0, which removes the Azure and Entra ID authentication dependencies (`Azure.Core`, `Azure.Identity`, `Microsoft.Identity.Client`) from the core package.

**Fix:** If the app uses `ActiveDirectoryDefault`, `ActiveDirectoryInteractive`, `ActiveDirectoryManagedIdentity`, or `ActiveDirectoryServicePrincipal`, add:

```xml
<PackageReference Include="Microsoft.Data.SqlClient.Extensions.Azure" Version="7.0.0" />
```

`SqlAuthenticationMethod.ActiveDirectoryPassword` is obsolete; move to `ActiveDirectoryDefault` or `ActiveDirectoryInteractive`.

Source: https://github.com/dotnet/efcore/pull/37949

### `SqlVector<T>` properties are not loaded by default

**Impact: Low.** `SqlVector<T>` properties are excluded from `SELECT` and are `null` on materialized entities. They still work in `WHERE` and `ORDER BY`, including `VectorDistance()` and `VectorSearch()`.

**Fix:** Use an explicit projection to read vector values: `.Select(b => new { b.Id, b.Embedding })`.

Source: https://github.com/dotnet/efcore/pull/37829

## SQLite Provider

### Some SQLitePCLRaw bundle packages are no longer maintained

**Impact: Medium.** `Microsoft.Data.Sqlite` 11 uses SQLitePCLRaw 3.x. `SQLitePCLRaw.bundle_e_sqlcipher`, `bundle_sqlite3`, `bundle_winsqlite3`, `bundle_green`, and `bundle_e_sqlite3mc` are no longer updated and conflict with SQLitePCLRaw.Core 3.x.

**Fix:**
- Encrypted databases (SQLCipher): use SQLite3 Multiple Ciphers (`SQLite3MC.PCLRaw.bundle`), SQLite Encryption Extension, or SQLCipher from Zetetic. With `SQLite3MC.PCLRaw.bundle`, reference `Microsoft.EntityFrameworkCore.Sqlite.Core` (or `Microsoft.Data.Sqlite.Core` without EF Core) instead of the main package, so the default bundle is not loaded alongside it. SQLite3 Multiple Ciphers uses a different cipher by default, so replacing the bundle alone leaves an existing SQLCipher database unreadable. Select the SQLCipher scheme with URI parameters, using the `legacy` value that matches the SQLCipher version that created the database: `Data Source=file:example.db?cipher=sqlcipher&legacy=4`.
- `bundle_sqlite3` or `bundle_winsqlite3`: use `SQLitePCLRaw.provider.sqlite3` or `SQLitePCLRaw.provider.winsqlite3` and call `SQLitePCL.raw.SetProvider(...)` at startup.
- `bundle_green`: use `SQLitePCLRaw.bundle_e_sqlite3`.

### Microsoft.Data.Sqlite no longer supports .NET Framework

**Impact: Medium.** The packages target `net10.0` and later only. Projects that multi-target .NET Framework must stay on the latest 10.0.x release of `Microsoft.Data.Sqlite` for that target. UWP and classic Xamarin are no longer supported either.

## All Providers

**Impact: Low.**
- **Migrations not found throws:** `Migrate()` or `MigrateAsync()` with no migrations in the assembly now throws. If intentional, use `options.ConfigureWarnings(w => w.Ignore(RelationalEventId.MigrationsNotFound))`.
- **Scaffolded migration class names include the migration ID** (for example `_20261118152232_AddBlog`). This can trigger CA1707; suppress it for `Migrations/*.cs` in `.editorconfig` if needed.
- **EF tools packages no longer reference Design:** `Microsoft.EntityFrameworkCore.Tools` and `.Tasks` no longer depend on `Microsoft.EntityFrameworkCore.Design`. Add `<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="11.0.0" PrivateAssets="all" />` if the project relied on it.
- **`EFOptimizeContext` removed:** replace it with `<EFScaffoldModelStage>` and `<EFPrecompileQueriesStage>` (`publish` or `build`). With `PublishAot=true`, generation runs on publish automatically.
- **Owned JSON collections without an explicit key are obsolete** (`OwnedEntityMappedToJsonCollectionWarning`). Use `ComplexCollection(b => b.Posts, b => b.ToJson())` or add a key.
- **`Property` no longer configures primitive collections:** use `PrimitiveCollection(b => b.Tags)`.
- **Split queries throw on concurrent modification** (`DbQueryConcurrencyException`). Retry, use a serializable or snapshot transaction, or use `AsSingleQuery()`.
- **Assigning the same owned instance to more than one navigation throws** `InvalidOperationException`. Give each navigation its own instance, or use a complex type.
