using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SkillCatalog.Api.Endpoints;
using SkillCatalog.Api.Options;
using SkillCatalog.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddResponseCompression();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddOptions<SkillCatalogOptions>().Bind(builder.Configuration.GetSection(SkillCatalogOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<SkillCatalogOptions>>().Value);
builder.Services.AddSingleton<CatalogSnapshotProvider>();
builder.Services.AddSingleton<SkillSearchService>();
builder.Services.AddSingleton<SkillPackageService>();
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
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.Run();

public partial class Program;
