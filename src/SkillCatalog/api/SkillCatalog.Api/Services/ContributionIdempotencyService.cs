using Microsoft.EntityFrameworkCore;
using SkillCatalog.Api.Persistence;

namespace SkillCatalog.Api.Services;

public sealed class ContributionIdempotencyService(GitHubSubmissionDbContext db, TimeProvider time)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    public async Task<(IdempotencyLease Lease, Contribution? Existing)> AcquireAsync(
        long userId,
        string key,
        Guid intentId,
        CancellationToken cancellationToken)
    {
        var lease = await db.IdempotencyLeases.SingleOrDefaultAsync(
            x => x.ContributorGitHubUserId == userId && x.IdempotencyKey == key,
            cancellationToken);
        if (lease?.ContributionId is Guid existingId)
        {
            return (lease, await db.Contributions.FindAsync([existingId], cancellationToken));
        }
        if (lease is not null && lease.LeaseExpiresAt > time.GetUtcNow())
        {
            throw new InvalidOperationException("Submission is already in progress.");
        }

        if (lease is null)
        {
            lease = new IdempotencyLease { ContributorGitHubUserId = userId, IdempotencyKey = key };
            db.Add(lease);
        }
        lease.SubmissionIntentId = intentId;
        lease.LeaseOwner = Guid.NewGuid().ToString("N");
        lease.LeaseExpiresAt = time.GetUtcNow().Add(LeaseDuration);
        await db.SaveChangesAsync(cancellationToken);
        return (lease, null);
    }

    public async Task RenewAsync(IdempotencyLease lease, string leaseOwner, CancellationToken cancellationToken)
    {
        if (lease.CompletedAt is not null || !CryptographicEquals(lease.LeaseOwner, leaseOwner))
        {
            throw new InvalidOperationException("The idempotency lease is no longer owned by this operation.");
        }
        lease.LeaseExpiresAt = time.GetUtcNow().Add(LeaseDuration);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(IdempotencyLease lease, Guid contributionId, CancellationToken cancellationToken)
    {
        lease.ContributionId = contributionId;
        lease.CompletedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool CryptographicEquals(string left, string right)
    {
        var leftHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(left));
        var rightHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(right));
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }
}
