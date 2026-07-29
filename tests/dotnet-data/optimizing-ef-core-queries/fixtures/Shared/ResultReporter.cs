using System.Globalization;
using BenchmarkDotNet.Reports;

namespace OptimizingEfCoreQueries.Shared;

// Turns a BenchmarkDotNet Summary into a single, easy-to-grade line on stdout,
// e.g.
//
//   RESULT scenario=NPlusOneBenchmark baselineNs=1234567 optimizedNs=45678 ratio=0.037 speedup=27.03x improved=True
//
// A scenario "improves" when the optimized benchmark is at least
// ImprovementThreshold faster than the baseline. The margin is deliberately
// generous relative to run-to-run noise so the grader's `improved=True` check is
// not flaky, while still requiring a real, measurable win.
public static class ResultReporter
{
    // Optimized must be at most 90% of the baseline mean (i.e. >= ~1.1x faster).
    public const double ImprovementThreshold = 0.90;

    public static void Report(Summary summary)
    {
        var scenario = summary.BenchmarksCases.Length > 0
            ? summary.BenchmarksCases[0].Descriptor.Type.Name
            : "unknown";

        var baseline = summary.Reports.FirstOrDefault(r => r.BenchmarkCase.Descriptor.Baseline);
        var optimized = summary.Reports.FirstOrDefault(r => !r.BenchmarkCase.Descriptor.Baseline);

        if (baseline?.ResultStatistics is null || optimized?.ResultStatistics is null)
        {
            Console.WriteLine($"RESULT scenario={scenario} error=missing-benchmark-results improved=False");
            return;
        }

        var baselineNs = baseline.ResultStatistics.Mean;
        var optimizedNs = optimized.ResultStatistics.Mean;
        var ratio = baselineNs <= 0 ? double.NaN : optimizedNs / baselineNs;
        var speedup = optimizedNs <= 0 ? double.NaN : baselineNs / optimizedNs;
        var improved = ratio < ImprovementThreshold;

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"RESULT scenario={scenario} baselineNs={baselineNs:F0} optimizedNs={optimizedNs:F0} ratio={ratio:F3} speedup={speedup:F2}x improved={improved}"));
    }
}
