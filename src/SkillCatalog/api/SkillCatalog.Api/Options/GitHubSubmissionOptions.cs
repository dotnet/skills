using System.ComponentModel.DataAnnotations;

namespace SkillCatalog.Api.Options;

public sealed class GitHubSubmissionOptions
{
    public const string SectionName = "GitHubSubmission";
    [Required] public string ApiBaseUrl { get; init; } = "https://api.github.com";
    [Required] public string ApiVersion { get; init; } = "2022-11-28";
    public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    public string WebhookSecret { get; init; } = "";
    public string DataProtectionKeyPath { get; init; } = "";
    [Required] public string TargetOwner { get; init; } = "JonC613";
    [Required] public string TargetRepository { get; init; } = "skills";
    [Required] public string BaseBranch { get; init; } = "main";
    public string CallbackPath { get; init; } = "/api/auth/github/callback";
    public string[] AllowedOrigins { get; init; } = ["http://localhost:5173", "http://127.0.0.1:5173"];
    [Range(60, 1800)] public int AuthorizationLifetimeSeconds { get; init; } = 300;
    [Range(60, 86400)] public int IntentLifetimeSeconds { get; init; } = 1800;
    [Range(1, 10)] public int MaxRetries { get; init; } = 3;
    [Range(1, 365)] public int RetentionDays { get; init; } = 90;
    [Range(5, 300)] public int StatusRefreshSeconds { get; init; } = 30;
    [Range(1024, 1048576)] public int MaxWebhookBytes { get; init; } = 262144;
}

