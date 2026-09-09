using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contoso.Sales.Operations;

namespace Contoso.Sales.Operations.Benchmarks;

// Per-method micro-benchmarks for the SalesOperationsService audit.
//
// For every benchmarked method the harness:
//   1. runs the preserved original body (BaselineSalesOperationsService) and the
//      agent's edited SalesOperationsService over identical, freshly-seeded data
//      and checks they return the SAME result (a fast-but-wrong rewrite fails), and
//   2. times both over the same seeded in-memory SQLite database and reports a
//      RESULT line. A method is "improved" only when the edited version is
//      equivalent AND at least 10% faster.
//
// Timing model per method:
//   read      - a fresh context per timed call (one context per unit of work)
//   hot        - one long-lived context reused across many calls (pooled hot path)
//   mutation   - a fresh database seeded per iteration, one call timed
internal static class Program
{
    private const double ImprovementThreshold = 0.90; // optimized <= 90% of baseline

    private static readonly (string Name, Func<Result> Run)[] Benchmarks =
    {
        ("GetCustomerSales", BenchGetCustomerSales),
        ("GetCustomerDetail", BenchGetCustomerDetail),
        ("HasOrders", BenchHasOrders),
        ("GetOrdersOverTotal", BenchGetOrdersOverTotal),
        ("GetPendingOrders", BenchGetPendingOrders),
        ("GetUnpaidInvoices", BenchGetUnpaidInvoices),
        ("ListActiveProducts", BenchListActiveProducts),
        ("SearchProducts", BenchSearchProducts),
        ("GetProductForCard", BenchGetProductForCard),
        ("ApplyCategoryDiscount", BenchApplyCategoryDiscount),
        ("CountActiveProducts", BenchCountActiveProducts),
    };

    private static int Main(string[] args)
    {
        string? only = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--method" or "-m")
            {
                only = args[i + 1];
            }
        }

        var selected = only is null
            ? Benchmarks
            : Benchmarks.Where(b => string.Equals(b.Name, only, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (selected.Length == 0)
        {
            Console.Error.WriteLine($"Unknown --method '{only}'. Known methods: {string.Join(", ", Benchmarks.Select(b => b.Name))}");
            return 2;
        }

        var improved = 0;
        var failed = false;
        foreach (var (name, run) in selected)
        {
            Result r;
            try
            {
                r = run();
            }
            catch (Exception ex)
            {
                failed = true;
                Console.WriteLine($"RESULT method={name} baselineMs=0.00 optimizedMs=0.00 ratio=0.000 speedupPct=0.0 improved=False equivalent=False");
                Console.WriteLine($"  ERROR: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            var ratio = r.BaselineMs <= 0 ? 1.0 : r.OptimizedMs / r.BaselineMs;
            var isImproved = r.Equivalent && r.OptimizedMs <= r.BaselineMs * ImprovementThreshold;
            if (isImproved)
            {
                improved++;
            }
            else
            {
                failed = true;
            }

            Console.WriteLine(
                $"RESULT method={name} " +
                $"baselineMs={r.BaselineMs.ToString("F2", CultureInfo.InvariantCulture)} " +
                $"optimizedMs={r.OptimizedMs.ToString("F2", CultureInfo.InvariantCulture)} " +
                $"ratio={ratio.ToString("F3", CultureInfo.InvariantCulture)} " +
                $"speedupPct={((1 - ratio) * 100).ToString("F1", CultureInfo.InvariantCulture)} " +
                $"improved={(isImproved ? "True" : "False")} " +
                $"equivalent={(r.Equivalent ? "True" : "False")}");
        }

        if (only is null)
        {
            Console.WriteLine($"SUITE improved={improved} total={selected.Length}");
            Console.WriteLine(!failed ? "SUITE-PASS" : "SUITE-FAIL");
        }

        // A single-method run is a benchmark probe: the grader checks the RESULT line,
        // so exit 0 even when the method was not improved. Only a hard failure
        // (exception / non-equivalence) is a non-zero exit for the whole-suite run.
        return only is null && failed ? 1 : 0;
    }

    // ----- read benchmarks -------------------------------------------------

    private static Result BenchRead(
        Action<SqliteConnection> seed,
        Func<SalesDbContext, string> baseline,
        Func<SalesDbContext, string> optimized,
        int warmup = 3,
        int iterations = 9)
    {
        var equal = Equivalent(seed, baseline, optimized);

        // Each arm gets its own seeded connection so one arm's warm SQLite page
        // cache cannot flatter the other.
        using var connB = Timing.OpenSharedConnection();
        seed(connB);
        using var connO = Timing.OpenSharedConnection();
        seed(connO);

        var (baselineMs, optimizedMs) = Timing.MedianPair(
            () =>
            {
                using var db = Timing.NewContext(connB);
                baseline(db);
            },
            () =>
            {
                using var db = Timing.NewContext(connO);
                optimized(db);
            },
            warmup,
            iterations);

        return new Result(baselineMs, optimizedMs, equal);
    }

    private static Result BenchGetCustomerSales() => BenchRead(
        Seed.CustomerSales,
        db => CanonCustomerSales(new BaselineSalesOperationsService(db).GetCustomerSales(2024)),
        db => CanonCustomerSales(new SalesOperationsService(db).GetCustomerSales(2024)));

    private static Result BenchGetCustomerDetail() => BenchRead(
        Seed.CustomerDetail,
        db => CanonDetail(new BaselineSalesOperationsService(db).GetCustomerDetail(Seed.TargetCustomerId)),
        db => CanonDetail(new SalesOperationsService(db).GetCustomerDetail(Seed.TargetCustomerId)),
        warmup: 2, iterations: 7);

    private static Result BenchHasOrders() => BenchRead(
        Seed.HasOrders,
        db => new BaselineSalesOperationsService(db).HasOrders(Seed.TargetCustomerId).ToString(),
        db => new SalesOperationsService(db).HasOrders(Seed.TargetCustomerId).ToString());

    private static Result BenchGetOrdersOverTotal() => BenchRead(
        Seed.Orders,
        db => CanonOrderRows(new BaselineSalesOperationsService(db).GetOrdersOverTotal(Seed.MinTotal)),
        db => CanonOrderRows(new SalesOperationsService(db).GetOrdersOverTotal(Seed.MinTotal)));

    private static Result BenchGetPendingOrders() => BenchRead(
        Seed.Orders,
        db => CanonOrderRows(new BaselineSalesOperationsService(db).GetPendingOrders()),
        db => CanonOrderRows(new SalesOperationsService(db).GetPendingOrders()));

    private static Result BenchSearchProducts() => BenchRead(
        Seed.Products,
        db => CanonCards(new BaselineSalesOperationsService(db).SearchProducts("Product 1")),
        db => CanonCards(new SalesOperationsService(db).SearchProducts("Product 1")));

    private static Result BenchGetUnpaidInvoices() => BenchRead(
        Seed.UnpaidInvoices,
        db => CanonInvoices(new BaselineSalesOperationsService(db).GetUnpaidInvoices()),
        db => CanonInvoices(new SalesOperationsService(db).GetUnpaidInvoices()));

    private static Result BenchListActiveProducts() => BenchRead(
        Seed.Products,
        db => CanonCards(new BaselineSalesOperationsService(db).ListActiveProducts(Seed.TargetCategoryId)),
        db => CanonCards(new SalesOperationsService(db).ListActiveProducts(Seed.TargetCategoryId)));

    private static Result BenchCountActiveProducts() => BenchRead(
        Seed.Products,
        db => new BaselineSalesOperationsService(db).CountActiveProducts().ToString(),
        db => new SalesOperationsService(db).CountActiveProducts().ToString());

    // ----- hot path (context reused across many calls) ---------------------

    private static Result BenchGetProductForCard()
    {
        // ~3000 lookups over a reused context: the per-call query-building overhead
        // dominates, which is exactly what a compiled query removes.
        var ids = new int[3000];
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = 1 + (i % 200);
        }

        string baseline, optimized;
        using (var cB = Timing.OpenSharedConnection())
        {
            Seed.Products(cB);
            using var db = Timing.NewContext(cB);
            var svc = new BaselineSalesOperationsService(db);
            baseline = string.Join("|", ids.Take(50).Select(id => CanonCard(svc.GetProductForCard(id))));
        }

        using (var cO = Timing.OpenSharedConnection())
        {
            Seed.Products(cO);
            using var db = Timing.NewContext(cO);
            var svc = new SalesOperationsService(db);
            optimized = string.Join("|", ids.Take(50).Select(id => CanonCard(svc.GetProductForCard(id))));
        }

        var equal = baseline == optimized;

        using var connB = Timing.OpenSharedConnection();
        Seed.Products(connB);
        using var connO = Timing.OpenSharedConnection();
        Seed.Products(connO);

        var (baselineMs, optimizedMs) = Timing.MedianPair(
            () =>
            {
                using var db = Timing.NewContext(connB);
                var svc = new BaselineSalesOperationsService(db);
                foreach (var id in ids)
                {
                    svc.GetProductForCard(id);
                }
            },
            () =>
            {
                using var db = Timing.NewContext(connO);
                var svc = new SalesOperationsService(db);
                foreach (var id in ids)
                {
                    svc.GetProductForCard(id);
                }
            },
            warmup: 1,
            iterations: 5);

        return new Result(baselineMs, optimizedMs, equal);
    }

    // ----- mutation (fresh database seeded per iteration) ------------------

    private static Result BenchApplyCategoryDiscount()
    {
        const decimal factor = 0.9m;

        string RunOn(Action<SalesDbContext> apply)
        {
            using var conn = Timing.OpenSharedConnection();
            Seed.Products(conn);
            using (var db = Timing.NewContext(conn))
            {
                apply(db);
            }

            using var verify = Timing.NewContext(conn);
            return CanonPrices(verify, Seed.TargetCategoryId);
        }

        var baseline = RunOn(db => new BaselineSalesOperationsService(db).ApplyCategoryDiscount(Seed.TargetCategoryId, factor));
        var optimized = RunOn(db => new SalesOperationsService(db).ApplyCategoryDiscount(Seed.TargetCategoryId, factor));
        var equal = baseline == optimized;

        // Reseed a fresh database for every timed call and interleave the two arms.
        const int warmup = 1;
        const int iterations = 3;
        var baselineSamples = new List<double>();
        var optimizedSamples = new List<double>();
        for (var i = 0; i < warmup + iterations; i++)
        {
            var b = TimeOneMutation(db => new BaselineSalesOperationsService(db).ApplyCategoryDiscount(Seed.TargetCategoryId, factor));
            var o = TimeOneMutation(db => new SalesOperationsService(db).ApplyCategoryDiscount(Seed.TargetCategoryId, factor));
            if (i >= warmup)
            {
                baselineSamples.Add(b);
                optimizedSamples.Add(o);
            }
        }

        return new Result(Timing.Median(baselineSamples), Timing.Median(optimizedSamples), equal);
    }

    private static double TimeOneMutation(Action<SalesDbContext> run)
    {
        using var conn = Timing.OpenSharedConnection();
        Seed.Products(conn);
        using var db = Timing.NewContext(conn);
        var sw = Stopwatch.StartNew();
        run(db);
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    // ----- equivalence + canonical result helpers --------------------------

    private static bool Equivalent(
        Action<SqliteConnection> seed,
        Func<SalesDbContext, string> baseline,
        Func<SalesDbContext, string> optimized)
    {
        string b, o;
        using (var cB = Timing.OpenSharedConnection())
        {
            seed(cB);
            using var db = Timing.NewContext(cB);
            b = baseline(db);
        }

        using (var cO = Timing.OpenSharedConnection())
        {
            seed(cO);
            using var db = Timing.NewContext(cO);
            o = optimized(db);
        }

        return b == o;
    }

    private static string Money(decimal d) => Math.Round(d, 2).ToString(CultureInfo.InvariantCulture);

    private static string CanonCustomerSales(List<CustomerSalesRow> rows) =>
        string.Join("|", rows
            .OrderBy(r => r.CustomerName, StringComparer.Ordinal)
            .Select(r => $"{r.CustomerName}:{r.OrderCount}:{Money(r.Revenue)}"));

    private static string CanonDetail(Customer c) =>
        $"orders={c.Orders.Count};lines={c.Orders.Sum(o => o.Lines.Count)};invoices={c.Invoices.Count};revenue={Money(c.Orders.Sum(o => o.Total))}";

    private static string CanonInvoices(List<Invoice> invoices) =>
        $"count={invoices.Count};amount={Money(invoices.Sum(i => i.Amount))}";

    private static string CanonCards(List<ProductCard> cards) =>
        string.Join("|", cards.OrderBy(c => c.Id).Select(CanonCard));

    private static string CanonOrderRows(List<OrderRow> rows) =>
        string.Join("|", rows
            .OrderBy(r => r.OrderId)
            .Select(r => $"{r.OrderId}:{r.CreatedAt.Ticks}:{Money(r.Total)}"));

    private static string CanonCard(ProductCard c) => $"{c.Id}:{c.Name}:{Money(c.Price)}";

    private static string CanonPrices(SalesDbContext db, int categoryId) =>
        string.Join("|", db.Products
            .Where(p => p.CategoryId == categoryId)
            .OrderBy(p => p.Id)
            .Select(p => new { p.Price, p.UpdatedAt })
            .ToList()
            .Select(p => $"{Money(p.Price)}:{(p.UpdatedAt != new DateTime(2024, 1, 1) ? 1 : 0)}"));
}

internal readonly record struct Result(double BaselineMs, double OptimizedMs, bool Equivalent);
