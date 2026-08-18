using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SkillCatalog.Api.Options;
using SkillCatalog.Api.Persistence;

namespace SkillCatalog.Api.Services;

public sealed class GitHubSubmissionDatabaseHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GitHubSubmissionDbContext>();
        return await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy("GitHub submission database is reachable.")
            : HealthCheckResult.Unhealthy("GitHub submission database is unreachable.");
    }
}

public sealed class GitHubSubmissionConfigurationHealthCheck(IOptions<GitHubSubmissionOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var value = options.Value;
        var missing = new[]
        {
            ("ClientId", value.ClientId), ("ClientSecret", value.ClientSecret),
            ("WebhookSecret", value.WebhookSecret), ("DataProtectionKeyPath", value.DataProtectionKeyPath)
        }.Where(item => string.IsNullOrWhiteSpace(item.Item2)).Select(item => item.Item1).ToArray();
        if (missing.Length > 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy($"Missing GitHub submission configuration: {string.Join(", ", missing)}."));
        }
        try
        {
            var directory = new DirectoryInfo(value.DataProtectionKeyPath);
            if (!directory.Exists)
                return Task.FromResult(HealthCheckResult.Unhealthy("The durable data-protection key directory does not exist."));
            _ = directory.EnumerateFileSystemInfos().Take(1).ToArray();
            return Task.FromResult(HealthCheckResult.Healthy("GitHub configuration and durable key directory are available."));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("The durable data-protection key directory is not accessible.", exception));
        }
    }
}

