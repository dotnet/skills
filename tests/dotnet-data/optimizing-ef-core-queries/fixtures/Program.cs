using System.Reflection;
using BenchmarkDotNet.Running;
using OptimizingEfCoreQueries.Shared;

// Entry point. The grader runs a single scenario with, for example:
//
//   dotnet run -c Release -- --filter *NPlusOneBenchmark*
//
// BenchmarkDotNet executes the matching Baseline/Optimized pair and we print a
// machine-readable RESULT line per scenario for the grader to assert on.
var summaries = BenchmarkSwitcher
    .FromAssembly(Assembly.GetExecutingAssembly())
    .Run(args);

foreach (var summary in summaries)
{
    ResultReporter.Report(summary);
}
