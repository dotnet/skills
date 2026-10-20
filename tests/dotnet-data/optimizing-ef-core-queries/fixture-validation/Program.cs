using System.Data.Common;
using Contoso.Commerce.Data;
using Contoso.Commerce.Imports;
using Contoso.Commerce.Routing;
using Contoso.Commerce.SavedSearch;
using Contoso.Commerce.Search;
using Contoso.Commerce.Services;
using Contoso.Commerce.Statements;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

var selectedCase = args switch
{
    [] => "all",
    ["--case", var value] => value,
    _ => throw new ArgumentException("Usage: dotnet run --project fixture-validation -- [--case <name>]")
};

await RunCase(selectedCase);

static async Task RunCase(string selectedCase)
{
    switch (selectedCase)
    {
        case "all":
            foreach (var caseName in new[]
            {
                "customer-search",
                "order-operations",
                "paging-sorting",
                "route-planning",
                "clean-data-access",
                "saved-search",
                "statement-export"
            })
            {
                await RunCase(caseName);
            }
            Console.WriteLine("All fixture invariants passed. SQL Server checks used metadata and query translation only.");
            break;
        case "customer-search":
            await CheckDirectoryContexts();
            CheckDirectoryTranslation();
            break;
        case "order-operations":
            await CheckQueueEnumeration();
            await CheckImportBoundaries();
            break;
        case "paging-sorting":
            CheckHistoryCursor();
            break;
        case "route-planning":
            CheckModelCoverage();
            break;
        case "clean-data-access":
            await CheckCleanDataAccess();
            break;
        case "saved-search":
            CheckSavedSearchConstruction();
            break;
        case "statement-export":
            await CheckFilteredCase();
            await CheckStatementGraph();
            await CheckPostingWrites();
            break;
        default:
            throw new ArgumentOutOfRangeException(nameof(selectedCase), selectedCase, "Unknown fixture validation case.");
    }
}

static async Task CheckFilteredCase()
{
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<StatementDbContext>().UseSqlite(connection).Options;
    await using (var setup = new StatementDbContext(options))
    {
        await setup.Database.EnsureCreatedAsync();
        setup.Accounts.Add(new StatementAccount { Id = 1, Name = "Cedar" });
        setup.Invoices.AddRange(
            new StatementInvoice { Id = 1, AccountId = 1, Amount = 10 },
            new StatementInvoice { Id = 2, AccountId = 1, Amount = 20, Paid = true });
        await setup.SaveChangesAsync();
    }

    await using var db = new StatementDbContext(options);
    var reminder = await new StatementExportService(new StatementRepository(db)).BuildReminderAsync(1, default);
    Require(reminder.LifetimeAmount == 30, "History total must be unchanged.");
    Require(reminder.UnpaidInvoiceIds.Order().SequenceEqual(new[] { 1, 2 }),
        "Tracked history must demonstrate the filtered-Include contamination.");
    Console.WriteLine("PASS reminders: paid history row reaches the unpaid subset through fixup.");
}

static async Task CheckDirectoryContexts()
{
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    connection.CreateCollation("Latin1_General_100_CI_AS",
        (first, second) => string.Compare(first, second, StringComparison.OrdinalIgnoreCase));
    var options = new DbContextOptionsBuilder<DirectoryDbContext>().UseSqlite(connection).Options;
    await using (var setup = new DirectoryDbContext(options))
    {
        await setup.Database.EnsureCreatedAsync();
        setup.Customers.AddRange(
            new DirectoryCustomer { Id = 1, Name = "Cedar", Region = "West" },
            new DirectoryCustomer { Id = 2, Name = "Maple", Region = "East" });
        setup.Products.Add(new CatalogProduct { Id = 1, Name = "Crate", Price = 10 });
        await setup.SaveChangesAsync();
    }

    var gate = new FirstReaderGate();
    var gatedOptions = new DbContextOptionsBuilder<DirectoryDbContext>()
        .UseSqlite(connection).AddInterceptors(gate).Options;
    var created = new List<DirectoryDbContext>();
    var factory = new DirectorySessionFactory(new FixtureContextFactory<DirectoryDbContext>(() =>
    {
        var db = new DirectoryDbContext(gatedOptions);
        created.Add(db);
        return db;
    }));
    var service = new DirectoryOverviewService(factory);
    var overlapping = service.GetOverviewAsync("West", default);
    await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
    gate.Release.SetResult();
    try
    {
        await overlapping;
        throw new InvalidOperationException("Expected the shared-context concurrency failure.");
    }
    catch (InvalidOperationException error) when (error.Message.Contains("second operation was started", StringComparison.Ordinal))
    {
    }

    Require(created.Count == 1, "Different readers must use one session context.");
    Require(await service.GetExportOverviewAsync("West", default) == new DirectoryOverview(1, 1),
        "Sequential counts must preserve their numeric results.");
    var beforeComparison = created.Count;
    Require(await service.GetRegionComparisonAsync("West", "East", default) == new RegionComparison(1, 1),
        "Independent sessions must preserve both region counts.");
    Require(created.Count == beforeComparison + 2 &&
        !ReferenceEquals(created[^1], created[^2]), "Parallel comparison must own two distinct contexts.");
    foreach (var db in created)
    {
        try
        {
            await db.Customers.CountAsync();
            throw new InvalidOperationException("Expected the owned session context to be disposed.");
        }
        catch (ObjectDisposedException)
        {
        }
    }

    Console.WriteLine("PASS directory: shared-context overlap fails; sequential and independently owned reads succeed.");
}

static async Task CheckQueueEnumeration()
{
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var counter = new ReaderCounter();
    var options = new DbContextOptionsBuilder<CommerceDbContext>()
        .UseSqlite(connection).AddInterceptors(counter).Options;
    await using var db = new CommerceDbContext(options);
    await db.Database.EnsureCreatedAsync();
    db.Customers.Add(new Customer { Id = 1, Name = "Cedar" });
    db.Orders.AddRange(
        new Order { Id = 1, TenantId = 7, CustomerId = 1, Status = "Open", Total = 10 },
        new Order { Id = 2, TenantId = 7, CustomerId = 1, Status = "Open", Total = 20, IsReviewed = true },
        new Order { Id = 3, TenantId = 7, CustomerId = 1, Status = "Closed", Total = 40 });
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    counter.Count = 0;

    var statistics = new OrderQueryService(db, new OrderReadRepository(db)).GetQueueStatistics(7);
    Require(statistics == new QueueStatistics(2, 1, 30), "Queue summaries must be numerically correct.");
    Require(counter.Count == 3, "IEnumerable aggregates must execute three database readers.");
    Console.WriteLine("PASS materialization: queue summary executes three full-row reads.");
}

static async Task CheckStatementGraph()
{
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<StatementDbContext>().UseSqlite(connection).Options;
    await using var db = new StatementDbContext(options);
    await db.Database.EnsureCreatedAsync();
    db.Accounts.Add(new StatementAccount
    {
        Id = 1,
        TenantId = 7,
        Name = "Cedar",
        ComplianceDocument = new byte[4096]
    });
    db.Invoices.AddRange(
        new StatementInvoice { Id = 1, AccountId = 1, Amount = 10 },
        new StatementInvoice { Id = 2, AccountId = 1, Amount = 20 });
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();

    var repository = new StatementRepository(db);
    var invoices = await repository.GetPaymentRunAsync(7, default);
    Require(!ReferenceEquals(invoices[0].Account, invoices[1].Account),
        "Plain no-tracking must create separate account references.");
    var file = await new StatementExportService(repository).BuildPaymentFileAsync(7, default);
    Require(file.Accounts.Count == 2 && file.Accounts.Select(account => account.AccountId).Distinct().Count() == 1,
        "The reference-keyed writer must demonstrate duplicate entries for one account ID.");

    await repository.GetCardsAsync(7, default);
    Require(db.ChangeTracker.Entries<StatementAccount>().Count() == 1,
        "An entity inside a record projection must still be tracked.");
    Require(!db.ChangeTracker.Entries<StatementInvoice>().Any(),
        "The scalar invoice portion of the record must not track invoice entities.");
    Console.WriteLine("PASS statements: reference duplication and entity-containing projection reproduced.");
}

static async Task CheckImportBoundaries()
{
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var saves = new SaveCounter();
    var detections = 0;
    var options = new DbContextOptionsBuilder<ImportDbContext>()
        .UseSqlite(connection)
        .AddInterceptors(saves)
        .LogTo(_ => detections++, new[] { CoreEventId.DetectChangesStarting }, LogLevel.Debug)
        .Options;
    await using (var setup = new ImportDbContext(options))
    {
        await setup.Database.EnsureCreatedAsync();
    }

    var factory = new PartnerImportSessionFactory(
        new FixtureContextFactory<ImportDbContext>(() => new ImportDbContext(options)));
    var service = new OrderImportService(factory);
    var rows = new[]
    {
        new IncomingOrder(7, 10, new DateTime(2026, 10, 1), 20),
        new IncomingOrder(7, 11, new DateTime(2026, 10, 2), 30),
        new IncomingOrder(7, 12, new DateTime(2026, 10, 3), 40)
    };
    saves.Count = 0;
    detections = 0;
    var ids = await service.ImportPartnerFeedAsync(rows, default);
    Require(saves.Count == 4, "Three journal saves must precede the final unit-of-work save.");
    Require(detections >= rows.Length * 2, "Per-row tracker inspection and persistence must trigger detection.");
    await using (var verify = new ImportDbContext(options))
    {
        var orders = await verify.Orders.ToDictionaryAsync(order => order.Id);
        Require(ids.Select(id => orders[id].Total).SequenceEqual(rows.Select(row => row.Total)),
            "Returned IDs must map to the original feed order through their entities.");
        Require(await verify.Receipts.CountAsync() == rows.Length, "Each accepted order must retain a receipt.");
    }

    saves.Count = 0;
    await service.ImportArchiveAsync(rows, default);
    Require(saves.Count == 1, "A three-row archive chunk must save once, not once per receipt.");
    Console.WriteLine("PASS imports: hidden saves, change-detection scans, ordered IDs, and archive batching verified.");
}

static async Task CheckPostingWrites()
{
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<StatementDbContext>().UseSqlite(connection).Options;
    await using (var setup = new StatementDbContext(options))
    {
        await setup.Database.EnsureCreatedAsync();
        setup.Accounts.Add(new StatementAccount
        {
            Id = 1, TenantId = 7, Name = "Cedar", PostedMinorUnits = 1000, Revision = 4
        });
        await setup.SaveChangesAsync();
    }

    await using (var db = new StatementDbContext(options))
    {
        var service = new StatementPostingService(new StatementPostingRepository(db));
        var result = await service.PostAsync(new PaymentPosting(7, 1, 4, 250, -1), default);
        db.ChangeTracker.Clear();
        var account = await db.Accounts.SingleAsync();
        Require(result == 999 && account.PostedMinorUnits == 999,
            "The tracked adjustment must demonstrate loss of the immediate 250-unit payment.");
        Require((await db.PostingReceipts.SingleAsync()).AmountMinorUnits == 249,
            "Receipt must expose the balance mismatch.");
    }

    var rejectedOptions = new DbContextOptionsBuilder<StatementDbContext>()
        .UseSqlite(connection).AddInterceptors(new RejectReceiptSave()).Options;
    await using (var db = new StatementDbContext(rejectedOptions))
    {
        var service = new StatementPostingService(new StatementPostingRepository(db));
        try
        {
            await service.PostAsync(new PaymentPosting(7, 1, 5, 300, -1), default);
            throw new InvalidOperationException("Expected the injected receipt failure.");
        }
        catch (ReceiptFailure)
        {
        }
    }

    await using var verify = new StatementDbContext(options);
    Require((await verify.Accounts.SingleAsync()).PostedMinorUnits == 1299,
        "The immediate update must remain committed after the separate receipt save fails.");
    Require(await verify.PostingReceipts.CountAsync() == 1, "The failed operation must not add a receipt.");
    await using var stale = new StatementDbContext(options);
    var repository = new StatementPostingRepository(stale);
    var snapshot = await repository.GetAccountAsync(7, 1, default);
    await verify.Accounts.ExecuteUpdateAsync(setters => setters.SetProperty(account => account.Revision, 6));
    Require(snapshot.Revision == 5, "The posting snapshot must now be stale.");
    Require(await repository.RecordPaymentAsync(1, 10, default) == 1,
        "Immediate payment must demonstrate bypass of the changed concurrency token.");
    verify.ChangeTracker.Clear();
    Require((await verify.Accounts.SingleAsync()).PostedMinorUnits == 1309,
        "The stale direct update must still have changed the balance.");
    Console.WriteLine("PASS posting: stale overwrite, revision bypass and non-atomic receipt failure reproduced.");
}

static async Task CheckCleanDataAccess()
{
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<CommerceDbContext>().UseSqlite(connection).Options;
    await using var db = new CommerceDbContext(options);
    await db.Database.EnsureCreatedAsync();
    db.Employees.Add(new Employee { Id = 1, DisplayName = "Avery" });
    db.Customers.Add(new Customer { Id = 1, TenantId = 7, AccountManagerId = 1, Name = "Cedar" });
    db.Orders.Add(new Order
    {
        Id = 1,
        TenantId = 7,
        CustomerId = 1,
        CreatedAt = new DateTime(2026, 10, 1),
        Total = 20,
        Status = "Open"
    });
    db.Invoices.Add(new Invoice { Id = 1, TenantId = 7, CustomerId = 1, Amount = 15 });
    db.AuditEntries.AddRange(
        new AuditEntry
        {
            Id = 1,
            TenantId = 7,
            CreatedAt = new DateTime(2026, 10, 2),
            Category = "Billing",
            Notes = "Expected"
        },
        new AuditEntry
        {
            Id = 2,
            TenantId = 7,
            CreatedAt = new DateTime(2026, 10, 3),
            Category = "Operations",
            Notes = "Excluded"
        });
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();

    var service = new CleanDataAccessService(db);
    Require(await service.HasOpenOrdersAsync(7, default), "The indexed existence query must find the open order.");
    Require((await service.GetCustomerAsync(1, default)).Orders.Count == 1,
        "The bounded customer graph must retain its order.");
    Require((await service.GetNextPageAsync(7, 0, 10, default)).Select(row => row.Id).SequenceEqual(new[] { 1 }),
        "The keyset page must preserve its ordered row.");
    Require(await service.BuildSnapshotAsync(1, default) == new BillingSnapshot(1, 15),
        "Sequential scalar queries must preserve both snapshot values.");
    var auditEntries = await service.GetAuditCategoryAsync("Billing", default);
    Require(auditEntries.Select(entry => entry.Id).SequenceEqual(new long[] { 1 }) &&
        auditEntries[0].Category == "Billing",
        "The parameterized audit query must return only the requested category.");
    Require((await service.GetReviewBatchAsync(7, default)).Count == 1,
        "The bounded review batch must include the unreviewed order.");
    await service.MarkReviewedAsync(7, new[] { 1 }, default);
    db.ChangeTracker.Clear();
    Require(await db.Orders.Where(order => order.Id == 1).Select(order => order.IsReviewed).SingleAsync(),
        "The bounded review mutation must save once and persist.");
    Console.WriteLine("PASS clean data access: sound bounded, sequential, and set-preserving paths execute correctly.");
}

static void CheckModelCoverage()
{
    var options = new DbContextOptionsBuilder<RouteDbContext>()
        .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=FixtureValidation;Trusted_Connection=True").Options;
    using var db = new RouteDbContext(options);
    var route = db.Model.FindEntityType(typeof(ShipmentRoute))
        ?? throw new InvalidOperationException("ShipmentRoute is missing from the model.");
    var indexes = route.GetIndexes().Select(index => index.Properties.Select(property => property.Name).ToArray()).ToArray();
    Require(indexes.Any(keys => keys.SequenceEqual(new[] { "CarrierId", "WarehouseId", "DepartsAt" })),
        "The configured route index must exist.");
    Require(indexes.Any(keys => keys.SequenceEqual(new[] { "WarehouseId", "CarrierId" })),
        "The FK convention must create a WarehouseId-leading index.");
    Require(!indexes.Any(keys => keys.Take(2).SequenceEqual(new[] { "WarehouseId", "DepartsAt" })),
        "The warehouse-wide departure query must lack its workload-specific prefix.");
    Console.WriteLine("PASS route model: convention-created FK index exists but date prefix is uncovered.");
}

static void CheckSavedSearchConstruction()
{
    var compilations = 0;
    var options = new DbContextOptionsBuilder<OrderSearchDbContext>()
        .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=FixtureValidation;Trusted_Connection=True")
        .LogTo(_ => compilations++, new[] { CoreEventId.QueryCompilationStarting }, LogLevel.Debug)
        .Options;
    using var db = new OrderSearchDbContext(options);
    var compiler = new OrderFilterCompiler();
    var first = db.Orders.Where(compiler.Build(new[] { new OrderFilter(SearchField.CustomerCode, "Cedar") })).ToQueryString();
    var second = db.Orders.Where(compiler.Build(new[] { new OrderFilter(SearchField.CustomerCode, "Maple") })).ToQueryString();
    Require(first.Contains("N'Cedar'", StringComparison.Ordinal) &&
        second.Contains("N'Maple'", StringComparison.Ordinal) &&
        !first.Contains("DECLARE @", StringComparison.Ordinal) &&
        !second.Contains("DECLARE @", StringComparison.Ordinal),
        "Dynamic filter values must appear as differing SQL literals.");

    var customerCode = "Cedar";
    var captured = db.Orders.Where(order => order.CustomerCode == customerCode).ToQueryString();
    Require(captured.Contains("DECLARE @", StringComparison.Ordinal), "Captured customer code must be parameterized.");

    compilations = 0;
    var literalFirst = db.Orders.Where(OrderQueryPolicies.CustomerActivity("Cedar")).ToQueryString();
    var literalSecond = db.Orders.Where(OrderQueryPolicies.CustomerActivity("Maple")).ToQueryString();
    Require(literalFirst.Contains("N'Cedar'", StringComparison.Ordinal) &&
        literalSecond.Contains("N'Maple'", StringComparison.Ordinal) &&
        !literalFirst.Contains("DECLARE @", StringComparison.Ordinal) &&
        !literalSecond.Contains("DECLARE @", StringComparison.Ordinal),
        "EF.Constant must specialize SQL for changing captured customer codes.");
    Require(compilations == 1, "Different EF.Constant values must still reuse EF's translation cache.");

    var parameter = db.Orders.Where(OrderQueryPolicies.Imported()).ToQueryString();
    var parameterWhere = parameter[parameter.IndexOf("WHERE", StringComparison.Ordinal)..];
    Require(parameter.Contains("DECLARE @", StringComparison.Ordinal) &&
        parameterWhere.Contains('@') &&
        !parameterWhere.Contains("N'Imported'", StringComparison.Ordinal),
        "EF.Parameter must force the fixed status into a SQL parameter.");
    var structural = db.Orders.Where(order => order.Status == "Imported").ToQueryString();
    Require(structural.Contains("N'Imported'", StringComparison.Ordinal) &&
        !structural.Contains("DECLARE @", StringComparison.Ordinal),
        "An ordinary fixed status must remain a SQL literal.");

    Console.WriteLine("PASS saved search: dynamic literals, parameter overrides and EF translation reuse verified.");
}

static void CheckDirectoryTranslation()
{
    var options = new DbContextOptionsBuilder<DirectoryDbContext>()
        .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=FixtureValidation;Trusted_Connection=True").Options;
    using var directory = new DirectoryDbContext(options);
    try
    {
        directory.Customers.Where(DirectoryMatching.NameHint("Ced")).ToQueryString();
        throw new InvalidOperationException("Expected the SQL Server StringComparison translation failure.");
    }
    catch (InvalidOperationException error) when (error.Message.Contains("could not be translated", StringComparison.Ordinal))
    {
    }

    Console.WriteLine("PASS directory query: unsupported StringComparison translation reproduced.");
}

static void CheckHistoryCursor()
{
    var timestamp = new DateTime(2026, 10, 1);
    var rows = new[]
    {
        new Order { Id = 200, CreatedAt = timestamp.AddDays(-1) },
        new Order { Id = 99, CreatedAt = timestamp },
        new Order { Id = 98, CreatedAt = timestamp.AddDays(-2) }
    };
    var result = HistoryQueryRules.After(rows.AsQueryable(), new HistoryCursor(timestamp, 100))
        .OrderByDescending(order => order.CreatedAt).ThenByDescending(order => order.Id)
        .Select(order => order.Id).ToArray();
    Require(result.SequenceEqual(new[] { 99, 98 }), "Independent ID bound must exclude the backfilled row 200.");
    Console.WriteLine("PASS history cursor: older backfill with higher ID is wrongly excluded.");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed class FixtureContextFactory<TContext>(Func<TContext> create) : IDbContextFactory<TContext>
    where TContext : DbContext
{
    public TContext CreateDbContext() => create();
    public Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(create());
    }
}

sealed class ReaderCounter : DbCommandInterceptor
{
    public int Count { get; set; }
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Count++;
        return result;
    }
}

sealed class SaveCounter : SaveChangesInterceptor
{
    public int Count { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Count++;
        return ValueTask.FromResult(result);
    }
}

sealed class FirstReaderGate : DbCommandInterceptor
{
    private int _seen;

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _seen, 1) == 0)
        {
            Started.SetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        }

        return result;
    }
}

sealed class ReceiptFailure : Exception;

sealed class RejectReceiptSave : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context?.ChangeTracker.Entries<PostingReceipt>()
            .Any(entry => entry.State == EntityState.Added) == true)
        {
            throw new ReceiptFailure();
        }

        return ValueTask.FromResult(result);
    }
}
