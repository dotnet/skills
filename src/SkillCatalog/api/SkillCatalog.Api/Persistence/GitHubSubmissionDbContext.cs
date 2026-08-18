using Microsoft.EntityFrameworkCore;

namespace SkillCatalog.Api.Persistence;

public sealed class GitHubSubmissionDbContext(DbContextOptions<GitHubSubmissionDbContext> options) : DbContext(options)
{
    public DbSet<AuthorizationTransaction> AuthorizationTransactions => Set<AuthorizationTransaction>();
    public DbSet<ContributorSession> ContributorSessions => Set<ContributorSession>();
    public DbSet<SubmissionIntent> SubmissionIntents => Set<SubmissionIntent>();
    public DbSet<Contribution> Contributions => Set<Contribution>();
    public DbSet<IdempotencyLease> IdempotencyLeases => Set<IdempotencyLease>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<AuditTransition> AuditTransitions => Set<AuditTransition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var authorization = modelBuilder.Entity<AuthorizationTransaction>();
        authorization.HasIndex(x => x.StateDigest).IsUnique();
        authorization.HasIndex(x => x.ExpiresAt);

        var session = modelBuilder.Entity<ContributorSession>();
        session.HasIndex(x => new { x.GitHubUserId, x.RevokedAt });
        session.HasIndex(x => x.AccessExpiresAt);
        session.Property(x => x.ProtectedAccessToken).IsRequired();

        var intent = modelBuilder.Entity<SubmissionIntent>();
        intent.HasIndex(x => new { x.ContributorSessionId, x.IdempotencyKey }).IsUnique();
        intent.HasIndex(x => x.ExpiresAt);
        intent.Property(x => x.FileManifestJson).HasColumnType("jsonb");

        var contribution = modelBuilder.Entity<Contribution>();
        contribution.HasIndex(x => x.SubmissionIntentId).IsUnique();
        contribution.HasIndex(x => new { x.ContributorGitHubUserId, x.UpdatedAt });
        contribution.Property(x => x.Version).IsRowVersion();

        var lease = modelBuilder.Entity<IdempotencyLease>();
        lease.HasKey(x => new { x.ContributorGitHubUserId, x.IdempotencyKey });
        lease.HasIndex(x => x.LeaseExpiresAt);

        var delivery = modelBuilder.Entity<WebhookDelivery>();
        delivery.HasIndex(x => x.ReceivedAt);

        var audit = modelBuilder.Entity<AuditTransition>();
        audit.HasIndex(x => new { x.ContributionId, x.OccurredAt });
        audit.HasIndex(x => x.OccurredAt);
    }
}
