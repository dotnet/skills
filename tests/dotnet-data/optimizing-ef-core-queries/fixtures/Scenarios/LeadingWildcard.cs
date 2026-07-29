using Microsoft.EntityFrameworkCore;
using OptimizingEfCoreQueries.Shared;

namespace OptimizingEfCoreQueries.Scenarios;

// SCENARIO: prefix search written with a leading-wildcard Contains.
//
// Run returns the ids of every article whose Reference starts with prefix, but it
// filters with Contains — which EF Core translates to a leading-wildcard match
// (LIKE '%prefix%' / instr) that cannot use an index and scans the full text of
// every row. Because the caller only wants a prefix match, rewrite the filter so
// the pattern is anchored to the start of the column, keeping the method name,
// signature and results unchanged. The LeadingWildcardBenchmark compares your
// version against the original.
public static class LeadingWildcardSolution
{
    public static List<int> Run(AppDbContext db, string prefix)
    {
        return db.Articles
            .Where(a => a.Reference.Contains(prefix))
            .OrderBy(a => a.Id)
            .Select(a => a.Id)
            .ToList();
    }
}
