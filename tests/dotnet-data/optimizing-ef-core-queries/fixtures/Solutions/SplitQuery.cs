using Microsoft.EntityFrameworkCore;
using OptimizingEfCoreQueries.Shared;

namespace OptimizingEfCoreQueries.Solutions;

// SCENARIO: Cartesian explosion from multiple Includes.
//
// Run eager-loads two independent collections (Posts and Contributors) in a
// single query. Because they are unrelated to each other, the relational join
// multiplies their rows together — a Cartesian explosion that returns Posts ×
// Contributors rows per blog. Rewrite the query so EF Core fetches each
// collection separately, keeping the method name, signature and returned data
// unchanged. The SplitQueryBenchmark compares your version against the original.
public static class SplitQuerySolution
{
    public static List<BlogWithChildren> Run(AppDbContext db)
    {
        var blogs = db.Blogs
            .Include(b => b.Posts)
            .Include(b => b.Contributors)
            .ToList();

        return blogs
            .Select(b => new BlogWithChildren(b.Name, b.Posts.Count, b.Contributors.Count))
            .ToList();
    }
}
