using Microsoft.EntityFrameworkCore;
using OptimizingEfCoreQueries.Shared;

namespace OptimizingEfCoreQueries.Solutions;

// SCENARIO: rebuilding a DbContext for every short-lived unit of work.
//
// AcquireContext is called once per simulated request. It news-up a fresh
// AppDbContext every time, so each request pays the full context-initialization
// cost. Rewrite it to hand out contexts from a reused DbContext pool
// (PooledDbContextFactory) instead of allocating a new one each call, keeping the
// method name and signature unchanged and returning a context the caller disposes
// (which returns it to the pool). The ContextPoolingBenchmark compares your
// version against the original.
public static class ContextPoolingSolution
{
    public static AppDbContext AcquireContext(DbContextOptions<AppDbContext> options)
    {
        return new AppDbContext(options);
    }
}
