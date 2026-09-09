using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contoso.Sales.Operations;

namespace Contoso.Sales.Operations.Benchmarks;

// Small timing helpers. EF Core over an in-memory SQLite database lands in the
// millisecond range, so measurements have to defend against three sources of bias:
//   * JIT + first-touch costs      -> warm both arms before timing anything
//   * the SQLite page cache        -> give each arm its OWN seeded connection
//   * slow global drift / GC       -> interleave the two arms and take medians
// With those in place two identical implementations time to ~1.0x of each other,
// so a reported speed-up reflects the code change rather than measurement order.
internal static class Timing
{
    // A fresh, short-lived context over the given connection — the normal
    // "one context per unit of work" pattern. The in-memory database lives for as
    // long as the connection stays open.
    public static SalesDbContext NewContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<SalesDbContext>().UseSqlite(connection).Options);

    public static SqliteConnection OpenSharedConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    // Times two arms fairly: both are warmed up first, then each iteration times
    // both arms, alternating which goes first and collecting garbage before each
    // timed region so neither arm is charged for the other's allocations or for a
    // measurement-order penalty. The median of each arm is returned.
    public static (double BaselineMs, double OptimizedMs) MedianPair(
        Action baseline,
        Action optimized,
        int warmup,
        int iterations)
    {
        for (var i = 0; i < warmup; i++)
        {
            baseline();
            optimized();
        }

        var baselineSamples = new double[iterations];
        var optimizedSamples = new double[iterations];
        var sw = new Stopwatch();
        for (var i = 0; i < iterations; i++)
        {
            if ((i & 1) == 0)
            {
                baselineSamples[i] = TimeOnce(sw, baseline);
                optimizedSamples[i] = TimeOnce(sw, optimized);
            }
            else
            {
                optimizedSamples[i] = TimeOnce(sw, optimized);
                baselineSamples[i] = TimeOnce(sw, baseline);
            }
        }

        return (Median(baselineSamples), Median(optimizedSamples));
    }

    private static double TimeOnce(Stopwatch sw, Action action)
    {
        // Start each measurement from an equally-clean heap so a tracking-heavy arm
        // is not charged for garbage the other arm produced.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        sw.Restart();
        action();
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    public static double Median(double[] samples)
    {
        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    public static double Median(List<double> samples)
    {
        samples.Sort();
        return samples[samples.Count / 2];
    }
}
