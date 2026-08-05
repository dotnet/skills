using CatalogMvc.Models;
using Microsoft.EntityFrameworkCore;

namespace CatalogMvc.Data;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Author> Authors => Set<Author>();

    public DbSet<Book> Books => Set<Book>();
}
