using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MessagingApi.Models;

namespace MessagingApi.Data;

public class MessagingDbContext(DbContextOptions<MessagingDbContext> options) : DbContext(options)
{
    public DbSet<MessagingNamespace> Namespaces => Set<MessagingNamespace>();
    public DbSet<Queue> Queues => Set<Queue>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<AuthorizationRule> AuthorizationRules => Set<AuthorizationRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var ns = modelBuilder.Entity<MessagingNamespace>();
        ns.HasIndex(n => n.Name).IsUnique();
        ns.HasMany(n => n.Queues).WithOne(q => q.Namespace!).HasForeignKey(q => q.NamespaceId);
        ns.HasMany(n => n.Topics).WithOne(t => t.Namespace!).HasForeignKey(t => t.NamespaceId);
        ns.HasMany(n => n.AuthorizationRules).WithOne(a => a.Namespace!).HasForeignKey(a => a.NamespaceId);

        var tagsConverter = new ValueConverter<Dictionary<string, string>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null) ?? new());
        var tagsComparer = new ValueComparer<Dictionary<string, string>>(
            (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null) ?? new());
        ns.Property(n => n.Tags).HasConversion(tagsConverter, tagsComparer);

        modelBuilder.Entity<Topic>()
            .HasMany(t => t.Subscriptions).WithOne(s => s.Topic!).HasForeignKey(s => s.TopicId);

        modelBuilder.Entity<Queue>().Property(q => q.LockDuration).HasConversion(new TimeSpanToTicksConverter());
    }
}
