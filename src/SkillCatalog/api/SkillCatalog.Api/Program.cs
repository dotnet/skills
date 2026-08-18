using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SkillCatalog.Api.Auth;
using SkillCatalog.Api.Endpoints;
using SkillCatalog.Api.GitHub;
using SkillCatalog.Api.Options;
using SkillCatalog.Api.Persistence;
using SkillCatalog.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = builder.Configuration.GetSection(SkillSubmissionOptions.SectionName).GetValue<long>("MaxRequestBytes", 2_000_000));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddResponseCompression();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-skillcatalog-csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddOptions<SkillCatalogOptions>().Bind(builder.Configuration.GetSection(SkillCatalogOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<SkillSubmissionOptions>().Bind(builder.Configuration.GetSection(SkillSubmissionOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<GitHubSubmissionOptions>().Bind(builder.Configuration.GetSection(GitHubSubmissionOptions.SectionName)).ValidateDataAnnotations();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<SkillCatalogOptions>>().Value);
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<SkillSubmissionOptions>>().Value);
var githubOptions = builder.Configuration.GetSection(GitHubSubmissionOptions.SectionName).Get<GitHubSubmissionOptions>() ?? new();
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("SkillCatalog");
if (!string.IsNullOrWhiteSpace(githubOptions.DataProtectionKeyPath)) dataProtection.PersistKeysToFileSystem(new DirectoryInfo(githubOptions.DataProtectionKeyPath));
builder.Services.AddDbContext<GitHubSubmissionDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("GitHubSubmissions") ?? "Host=localhost;Database=skillcatalog;Username=skillcatalog;Password=skillcatalog"));
builder.Services.AddHttpClient("GitHubOAuth");
builder.Services.AddHttpClient<IGitHubContributionClient, GitHubContributionClient>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<GitHubContributorAuthentication>();
builder.Services.AddScoped<RepositoryRevisionService>();
builder.Services.AddScoped<SkillUpdateContributionService>();
builder.Services.AddScoped<SubmissionIntentService>();
builder.Services.AddScoped<ContributionIdempotencyService>();
builder.Services.AddScoped<NewSkillContributionService>();
builder.Services.AddScoped<ContributionStatusService>();
builder.Services.AddScoped<GitHubWebhookProcessor>();
builder.Services.AddSingleton<GitHubSubmissionCleanupService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<GitHubSubmissionCleanupService>());
builder.Services.AddHealthChecks()
    .AddCheck<GitHubSubmissionDatabaseHealthCheck>("github-submission-database")
    .AddCheck<GitHubSubmissionConfigurationHealthCheck>("github-submission-configuration");
builder.Services.AddSingleton<CatalogSnapshotProvider>();
builder.Services.AddSingleton<SkillSearchService>();
builder.Services.AddSingleton<SkillPackageService>();
builder.Services.AddSingleton<SubmissionRuleProvider>();
builder.Services.AddSingleton<UploadedSkillValidator>();
builder.Services.AddSingleton<SkillPackageParser>();
builder.Services.AddSingleton<CatalogTelemetry>();

var app = builder.Build();
app.UseExceptionHandler(error => error.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await Results.Problem(title: "The request could not be completed.", statusCode: 500, extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
    app.Logger.LogError(feature?.Error, "Unhandled catalog request failure {TraceId}", context.TraceIdentifier);
}));
app.Use(async (context, next) => { context.Response.Headers["X-Content-Type-Options"] = "nosniff"; context.Response.Headers["Referrer-Policy"] = "no-referrer"; await next(); });
app.UseResponseCompression();
app.UseCors();
app.MapOpenApi();
app.MapCatalogEndpoints();
app.MapSkillEndpoints();
app.MapSubmissionEndpoints();
app.MapGitHubAuthenticationEndpoints();
app.MapGitHubSubmissionEndpoints();
app.MapGitHubContributionStatusEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapHealthChecks("/health/ready", new HealthCheckOptions { AllowCachingResponses = false });
app.Run();

public partial class Program;
