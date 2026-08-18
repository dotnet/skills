using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SkillCatalog.Api.Persistence;
using SkillCatalog.Api.GitHub;

namespace SkillCatalog.Api.ContractTests;

public sealed class CatalogApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"contract-tests-{Guid.NewGuid():N}";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GitHubSubmission:WebhookSecret"] = "contract-test-webhook-secret",
            ["GitHubSubmission:MaxWebhookBytes"] = "4096",
            ["GitHubSubmission:DataProtectionKeyPath"] = "C:\\tmp\\skillcatalog-contract-keys"
        }));
        builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole().AddProvider(new CapturingLoggerProvider()));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<GitHubSubmissionDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<GitHubSubmissionDbContext>>();
            services.RemoveAll<IDatabaseProvider>();
            services.AddDbContext<GitHubSubmissionDbContext>(o => o.UseInMemoryDatabase(_databaseName));
            services.RemoveAll<IGitHubContributionClient>();
            services.AddSingleton<IGitHubContributionClient, ContractGitHubClient>();
        });
    }
}
public sealed class ContractGitHubClient : IGitHubContributionClient
{
    public bool FailWithRateLimit { get; set; }
    public bool FailWithPartialSuccess { get; set; }
    public Task<GitHubRepositorySnapshot> GetTargetSnapshotAsync(string token,CancellationToken ct)=>Task.FromResult(new GitHubRepositorySnapshot("base",[]));
    public Task<GitHubRepository?> GetEligibleForkAsync(string token,string login,CancellationToken ct)=>FailWithRateLimit?Task.FromException<GitHubRepository?>(new GitHubRateLimitException(TimeSpan.FromSeconds(1))):Task.FromResult<GitHubRepository?>(new(login,"skills","main","base",true));
    public Task CreateBranchAsync(string token,string owner,string repository,string branch,string sha,CancellationToken ct)=>Task.CompletedTask;
    public Task UpdateBranchAsync(string token,string owner,string repository,string branch,string sha,CancellationToken ct)=>Task.CompletedTask;
    public Task<string> CreateCommitAsync(string token,string owner,string repository,string branch,string baseTree,IReadOnlyList<GitHubFileChange> changes,string message,CancellationToken ct)=>Task.FromResult("commit");
    public Task<GitHubPullRequest> CreatePullRequestAsync(string token,string headOwner,string headBranch,string title,string body,CancellationToken ct)=>FailWithPartialSuccess?Task.FromException<GitHubPullRequest>(new HttpRequestException("test failure")):Task.FromResult(new GitHubPullRequest(7,"https://github.com/dotnet/skills/pull/7","open","commit"));
    public Task<GitHubIdentity> GetIdentityAsync(string token,CancellationToken ct)=>Task.FromResult(new GitHubIdentity(42,"octocat"));
    public Task<IReadOnlyList<GitHubInstallation>> GetInstallationsAsync(string token,CancellationToken ct)=>Task.FromResult<IReadOnlyList<GitHubInstallation>>([]);
    public Task<GitHubPullRequest> GetPullRequestAsync(string token,int number,CancellationToken ct)=>Task.FromResult(new GitHubPullRequest(number,"https://github.com/dotnet/skills/pull/7","open","commit"));
    public Task<IReadOnlyList<GitHubCheck>> GetChecksAsync(string token,string sha,CancellationToken ct)=>Task.FromResult<IReadOnlyList<GitHubCheck>>([]);
    public Task<IReadOnlyList<GitHubReview>> GetReviewsAsync(string token,int pullRequestNumber,CancellationToken ct)=>Task.FromResult<IReadOnlyList<GitHubReview>>([]);
}
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public static System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CapturingLogger();
    public void Dispose() { }
    private sealed class CapturingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState,Exception?,string> formatter)
            => Messages.Enqueue(formatter(state,exception) + (exception is null ? "" : $" {exception.GetType().Name}: {exception.Message}"));
    }
}
