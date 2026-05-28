using Microsoft.EntityFrameworkCore;
class BlogContext : DbContext
{
    public DbSet<Blog> Blogs => Set<Blog>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Blog>().HasIndex(b => b.Name);
    }
}
class Blog { public string Id { get; set; } public string Name { get; set; } }
