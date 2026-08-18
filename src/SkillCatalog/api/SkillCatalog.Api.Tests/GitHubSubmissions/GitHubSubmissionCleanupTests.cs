using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SkillCatalog.Api.Options;
using SkillCatalog.Api.Persistence;
using SkillCatalog.Api.Services;

namespace SkillCatalog.Api.Tests.GitHubSubmissions;

public sealed class GitHubSubmissionCleanupTests
{
    [Fact]
    public async Task Cleanup_is_bounded_and_preserves_active_records()
    {
        var services = new ServiceCollection();
        var database = Guid.NewGuid().ToString();
        services.AddDbContext<GitHubSubmissionDbContext>(options => options.UseInMemoryDatabase(database));
        await using var provider = services.BuildServiceProvider();
        var now = DateTimeOffset.UtcNow;
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GitHubSubmissionDbContext>();
            db.AuthorizationTransactions.AddRange(Enumerable.Range(0, 120).Select(_ => new AuthorizationTransaction
            {
                StateDigest = Guid.NewGuid().ToString("N"), PkceVerifier = "verifier", OpenerOrigin = "https://localhost", ExpiresAt = now.AddMinutes(-1)
            }));
            db.AuthorizationTransactions.Add(new AuthorizationTransaction
            {
                StateDigest = "active", PkceVerifier = "verifier", OpenerOrigin = "https://localhost", ExpiresAt = now.AddMinutes(10)
            });
            db.ContributorSessions.Add(new ContributorSession
            {
                GitHubUserId = 42, GitHubLogin = "revoked", ProtectedAccessToken = "", AccessExpiresAt = now.AddDays(-100), RevokedAt = now.AddDays(-100)
            });
            await db.SaveChangesAsync();
        }

        var cleanup = new GitHubSubmissionCleanupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new GitHubSubmissionOptions { RetentionDays = 90 }),
            TimeProvider.System,
            NullLogger<GitHubSubmissionCleanupService>.Instance);
        var removed = await cleanup.CleanupOnceAsync(CancellationToken.None);
        Assert.InRange(removed, 100, 101);

        await using var verification = provider.CreateAsyncScope();
        var verifyDb = verification.ServiceProvider.GetRequiredService<GitHubSubmissionDbContext>();
        Assert.NotNull(await verifyDb.AuthorizationTransactions.SingleOrDefaultAsync(item => item.StateDigest == "active"));
        Assert.Equal(20, await verifyDb.AuthorizationTransactions.CountAsync(item => item.ExpiresAt < now));
        Assert.Empty(await verifyDb.ContributorSessions.ToListAsync());
    }
}
