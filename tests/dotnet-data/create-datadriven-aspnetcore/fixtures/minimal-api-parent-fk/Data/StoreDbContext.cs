using Microsoft.EntityFrameworkCore;
using ParentChildApi.Models;

namespace ParentChildApi.Data;

public class StoreDbContext(DbContextOptions<StoreDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Product> Products => Set<Product>();
}
