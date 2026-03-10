---
name: implementing-rate-limiting
description: >
  Implement .NET 7+ built-in rate limiting middleware (AddRateLimiter, UseRateLimiter) with correct
  algorithm selection, partitioning, and response handling. USE FOR: adding API rate limiting,
  per-client/per-IP/per-user throttling, configuring sliding window or token bucket algorithms,
  fixing rate limiter returning 503 instead of 429, fixing silently inactive rate limiting.
  DO NOT USE FOR: distributed rate limiting across multiple servers (need Redis-backed solution),
  rate limiting at API gateway/reverse proxy layer (YARP, nginx, Azure API Management),
  pre-.NET 7 projects (no built-in support).
---

# Implementing Rate Limiting in ASP.NET Core (.NET 7+)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| API endpoints to protect | Yes | Which routes need rate limiting |
| Rate limit requirements | Yes | Requests per window, per-client vs global |
| .NET version | No | Must be .NET 7+ for built-in support |

## Workflow

### Step 1: Choose the right algorithm

| Algorithm | Best For | Behavior |
|-----------|----------|----------|
| **Fixed Window** | Simple per-minute/per-hour limits | Counter resets at window boundary. ⚠️ Burst problem: 100 req at end of window + 100 at start of next = 200 in 1 second |
| **Sliding Window** | Smoother rate distribution | Divides window into segments, slides across time. Avoids burst problem |
| **Token Bucket** | Allowing controlled bursts | Tokens replenish at fixed rate, requests consume tokens. Good for APIs that should allow short bursts |
| **Concurrency Limiter** | Limiting simultaneous requests | Caps concurrent in-flight requests, not rate. Good for protecting expensive endpoints |

**Common mistake:** Using Fixed Window when you need smooth distribution. A fixed window of "100 per minute" allows 200 requests in 2 seconds if they straddle the window boundary.

### Step 2: Configure the rate limiter in Program.cs

```csharp
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using System.Threading.RateLimiting;

builder.Services.AddRateLimiter(options =>
{
    // CRITICAL: Set rejection status code — default is 503! Most APIs should use 429
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Global rate limiter: sliding window (avoids fixed window burst problem)
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        // Use X-Forwarded-For behind a reverse proxy, fall back to RemoteIpAddress
        var ipAddress = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: ipAddress,
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6, // 10-second segments for smooth distribution
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0 // Reject immediately, don't queue
            });
    });

    // Named policy: sliding window for sensitive endpoints
    options.AddSlidingWindowLimiter("api-sensitive", slidingOptions =>
    {
        slidingOptions.PermitLimit = 10;
        slidingOptions.Window = TimeSpan.FromMinutes(1);
        slidingOptions.SegmentsPerWindow = 6; // 10-second segments
        slidingOptions.QueueLimit = 0;
    });

    // Named policy: token bucket for search API (allows bursts)
    options.AddTokenBucketLimiter("search", tokenOptions =>
    {
        tokenOptions.TokenLimit = 20;         // Max burst size
        tokenOptions.ReplenishmentPeriod = TimeSpan.FromSeconds(10);
        tokenOptions.TokensPerPeriod = 5;     // 5 tokens every 10 seconds
        tokenOptions.QueueLimit = 0;
        tokenOptions.AutoReplenishment = true;
    });

    // Named policy: concurrency limiter for expensive operations
    options.AddConcurrencyLimiter("reports", concurrencyOptions =>
    {
        concurrencyOptions.PermitLimit = 5;   // Max 5 simultaneous report generations
        concurrencyOptions.QueueLimit = 10;
        concurrencyOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });

    // Custom response for rejected requests
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString();
        }

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Too many requests",
            retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var r)
                ? (int)r.TotalSeconds : 60
        }, cancellationToken);
    };
});
```

### Step 3: CRITICAL — Middleware ordering and application

```csharp
var app = builder.Build();

// Middleware ordering matters:
// UseRouting → UseAuthentication → UseAuthorization → UseRateLimiter
// Placing UseRateLimiter() AFTER UseAuthorization() lets you partition by authenticated user
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();       // ← AFTER auth so user claims are available for partitioning

// Apply policies to specific endpoints
app.MapGet("/api/search", SearchHandler)
    .RequireRateLimiting("search");

app.MapGet("/api/reports", ReportHandler)
    .RequireRateLimiting("reports");

// Apply to controller groups
app.MapGroup("/api/admin")
    .RequireRateLimiting("api-sensitive")
    .MapAdminEndpoints();

// Disable rate limiting for health checks
app.MapHealthChecks("/healthz")
    .DisableRateLimiting();

app.MapControllers();
```

**In controllers, use the attribute:**
```csharp
[EnableRateLimiting("api-sensitive")]
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    [DisableRateLimiting] // Override for specific action
    [HttpGet("status")]
    public IActionResult Status() => Ok();
}
```

### Step 4: Per-user/per-tenant partitioning

```csharp
// Per-authenticated-user rate limit
options.AddPolicy("per-user", context =>
{
    var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    return userId is not null
        ? RateLimitPartition.GetTokenBucketLimiter(userId, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 100,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            TokensPerPeriod = 50,
            AutoReplenishment = true
        })
        : RateLimitPartition.GetFixedWindowLimiter("anonymous", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1)
        });
});
```

### Step 5: Common configuration mistakes

| Mistake | Symptom | Fix |
|---------|---------|-----|
| Default `RejectionStatusCode` is 503 | Clients think server is down, not rate limited | Set `options.RejectionStatusCode = 429` |
| `UseRateLimiter()` before `UseRouting()` | Endpoint-specific policies silently don't apply | Move after `UseRouting()` and after `UseAuthorization()` so user claims are available for partitioning |
| Missing `RequireRateLimiting()` on endpoints | Global limiter works but named policies do nothing | Apply policies to endpoints explicitly |
| `AutoReplenishment = false` on token bucket | Tokens never replenish, all requests rejected after initial burst | Set `AutoReplenishment = true` (or use a background timer) |
| `QueueLimit > 0` without timeout | Requests queue indefinitely under sustained overload | Set `QueueLimit = 0` to reject immediately, or impose a timeout |
| Partitioning by `RemoteIpAddress` behind proxy | All requests share one IP (the proxy) | Partition by `X-Forwarded-For` header (as shown in Step 2 global limiter) or authenticated user |
| Not setting `Retry-After` header | Clients don't know when to retry | Use `OnRejected` callback with `MetadataName.RetryAfter` |

## Validation

- [ ] `RejectionStatusCode` set to 429 (not default 503)
- [ ] `UseRateLimiter()` called after `UseRouting()`
- [ ] Named policies applied to endpoints via `RequireRateLimiting()`
- [ ] Correct algorithm chosen for the use case (sliding window for smooth limits, token bucket for burst tolerance)
- [ ] Partitioning accounts for proxies (not just `RemoteIpAddress`)
- [ ] `OnRejected` returns proper error response with `Retry-After` header
- [ ] Health check and monitoring endpoints excluded from rate limiting

## Common Pitfalls

| Pitfall | Impact |
|---------|--------|
| Rate limiter silently inactive | `UseRateLimiter()` in wrong position, no error thrown |
| 503 instead of 429 on rate limit | Default status code misleads clients and monitoring |
| Fixed window burst problem | 2x expected traffic at window boundaries |
| Token bucket never replenishes | `AutoReplenishment = false` rejects everything after burst |
