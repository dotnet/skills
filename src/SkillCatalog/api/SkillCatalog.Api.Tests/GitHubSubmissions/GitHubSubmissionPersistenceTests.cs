using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SkillCatalog.Api.Persistence;

namespace SkillCatalog.Api.Tests.GitHubSubmissions;

public sealed class GitHubSubmissionPersistenceTests
{
    [Fact]
    public void Model_declares_uniqueness_concurrency_and_expiry_indexes()
    {
        using var db = CreateContext();
        var model = db.Model;

        var intent = model.FindEntityType(typeof(SubmissionIntent))!;
        Assert.Contains(intent.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(SubmissionIntent.ContributorSessionId), nameof(SubmissionIntent.IdempotencyKey)]));
        Assert.Contains(intent.GetIndexes(), index =>
            index.Properties.SingleOrDefault()?.Name == nameof(SubmissionIntent.ExpiresAt));

        var contribution = model.FindEntityType(typeof(Contribution))!;
        Assert.True(contribution.FindProperty(nameof(Contribution.Version))!.IsConcurrencyToken);

        var lease = model.FindEntityType(typeof(IdempotencyLease))!;
        Assert.Equal(
            [nameof(IdempotencyLease.ContributorGitHubUserId), nameof(IdempotencyLease.IdempotencyKey)],
            lease.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Contains(lease.GetIndexes(), index =>
            index.Properties.SingleOrDefault()?.Name == nameof(IdempotencyLease.LeaseExpiresAt));

        var delivery = model.FindEntityType(typeof(WebhookDelivery))!;
        Assert.Contains(delivery.GetIndexes(), index =>
            index.Properties.SingleOrDefault()?.Name == nameof(WebhookDelivery.ReceivedAt));
    }

    [Fact]
    public async Task Protected_credentials_are_not_stored_as_plaintext()
    {
        var keyDirectory = Path.Combine(Path.GetTempPath(), $"skillcatalog-keys-{Guid.NewGuid():N}");
        Directory.CreateDirectory(keyDirectory);
        try
        {
            var protector = DataProtectionProvider.Create(new DirectoryInfo(keyDirectory))
                .CreateProtector("SkillCatalog.GitHubTokens.v1");
            const string plaintext = "github-access-token-secret";
            var session = new ContributorSession
            {
                GitHubUserId = 42,
                GitHubLogin = "octocat",
                ProtectedAccessToken = protector.Protect(plaintext),
                AccessExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            };

            await using var db = CreateContext();
            db.Add(session);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var persisted = await db.ContributorSessions.SingleAsync();
            Assert.DoesNotContain(plaintext, persisted.ProtectedAccessToken, StringComparison.Ordinal);
            Assert.Equal(plaintext, protector.Unprotect(persisted.ProtectedAccessToken));
        }
        finally
        {
            Directory.Delete(keyDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Idempotency_key_replays_the_existing_lease()
    {
        await using var db = CreateContext();
        db.IdempotencyLeases.Add(new IdempotencyLease
        {
            ContributorGitHubUserId = 1,
            IdempotencyKey = "same",
            LeaseOwner = "a",
            LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1)
        });
        await db.SaveChangesAsync();

        var replay = await db.IdempotencyLeases.SingleAsync(x =>
            x.ContributorGitHubUserId == 1 && x.IdempotencyKey == "same");
        Assert.Equal("a", replay.LeaseOwner);
    }

    private static GitHubSubmissionDbContext CreateContext() => new(
        new DbContextOptionsBuilder<GitHubSubmissionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task PostgreSql_migration_enforces_uniqueness_concurrency_and_protected_credentials()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_POSTGRES_TESTS"), "1", StringComparison.Ordinal)) return;
        await using var postgres = new Testcontainers.PostgreSql.PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<GitHubSubmissionDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using (var setup = new GitHubSubmissionDbContext(options))
        {
            await setup.Database.MigrateAsync();
            var session = new ContributorSession { GitHubUserId = 42, GitHubLogin = "octocat", ProtectedAccessToken = "encrypted-ciphertext", AccessExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
            setup.Add(session);
            await setup.SaveChangesAsync();
            Assert.DoesNotContain("plain-token", (await setup.ContributorSessions.SingleAsync()).ProtectedAccessToken);
            setup.Add(SubmissionIntent.Create(session.Id, "hash", "NewSkill", "plugins/dotnet/skills/sample", "base", "duplicate"));
            await setup.SaveChangesAsync();
            setup.Add(SubmissionIntent.Create(session.Id, "hash2", "NewSkill", "plugins/dotnet/skills/sample2", "base", "duplicate"));
            await Assert.ThrowsAsync<DbUpdateException>(() => setup.SaveChangesAsync());
        }

        await using var seed = new GitHubSubmissionDbContext(options);
        var contribution = Contribution.Create(Guid.NewGuid(), 42, "octocat", "skills", "branch");
        seed.Add(contribution);
        await seed.SaveChangesAsync();
        await using var first = new GitHubSubmissionDbContext(options);
        await using var second = new GitHubSubmissionDbContext(options);
        var firstCopy = await first.Contributions.SingleAsync(x => x.Id == contribution.Id);
        var secondCopy = await second.Contributions.SingleAsync(x => x.Id == contribution.Id);
        firstCopy.UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(1);
        await first.SaveChangesAsync();
        secondCopy.UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(2);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }}


