using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SkillCatalog.Api.Options;
using SkillCatalog.Api.Persistence;

namespace SkillCatalog.Api.Services;

public sealed class GitHubSubmissionCleanupService(
    IServiceScopeFactory scopes,
    IOptions<GitHubSubmissionOptions> options,
    TimeProvider time,
    ILogger<GitHubSubmissionCleanupService> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private readonly GitHubSubmissionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await CleanupOnceAsync(stoppingToken); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "GitHub submission retention cleanup failed");
            }
        }
    }

    public async Task<int> CleanupOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GitHubSubmissionDbContext>();
        var now = time.GetUtcNow();
        var retention = now.AddDays(-_options.RetentionDays);
        var removed = 0;
        removed += await RemoveBatchAsync(db, db.AuthorizationTransactions.Where(item => item.ExpiresAt < now), cancellationToken);
        removed += await RemoveBatchAsync(db, db.ContributorSessions.Where(item =>
            (item.RevokedAt != null && item.RevokedAt < retention) ||
            (item.AccessExpiresAt < retention && (item.RefreshExpiresAt == null || item.RefreshExpiresAt < now))), cancellationToken);
        removed += await RemoveBatchAsync(db, db.SubmissionIntents.Where(item => item.ExpiresAt < now && !db.Contributions.Any(c => c.SubmissionIntentId == item.Id)), cancellationToken);
        removed += await RemoveBatchAsync(db, db.IdempotencyLeases.Where(item => item.LeaseExpiresAt < now && item.CompletedAt == null), cancellationToken);
        removed += await RemoveBatchAsync(db, db.WebhookDeliveries.Where(item => item.ReceivedAt < retention), cancellationToken);
        removed += await RemoveBatchAsync(db, db.AuditTransitions.Where(item => item.OccurredAt < retention), cancellationToken);
        return removed;
    }

    private static async Task<int> RemoveBatchAsync<TEntity>(GitHubSubmissionDbContext db, IQueryable<TEntity> query, CancellationToken cancellationToken)
        where TEntity : class
    {
        var items = await query.Take(BatchSize).ToListAsync(cancellationToken);
        if (items.Count == 0) return 0;
        db.RemoveRange(items);
        await db.SaveChangesAsync(cancellationToken);
        return items.Count;
    }
}
