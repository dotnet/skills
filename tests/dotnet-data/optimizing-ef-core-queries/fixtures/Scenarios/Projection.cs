using Microsoft.EntityFrameworkCore;
using OptimizingEfCoreQueries.Shared;

namespace OptimizingEfCoreQueries.Scenarios;

// SCENARIO: projection.
//
// Run returns a lightweight ProductListItem for every product in a category, but
// it first materializes whole Product entities — dragging along the heavy
// Description and Thumbnail columns the caller never uses. Rewrite the body so
// the database only returns the three columns the DTO needs (project with Select
// so the projection runs server-side), keeping the method name and signature
// unchanged. The ProjectionBenchmark compares your version against the original.
public static class ProjectionSolution
{
    public static List<ProductListItem> Run(AppDbContext db, int categoryId)
    {
        var products = db.Products
            .Where(p => p.CategoryId == categoryId)
            .ToList();

        return products
            .Select(p => new ProductListItem(p.Id, p.Name, p.Price))
            .ToList();
    }
}
