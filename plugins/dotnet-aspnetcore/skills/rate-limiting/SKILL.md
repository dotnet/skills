---
name: rate-limiting
description: >
  Protect an ASP.NET Core API from being overwhelmed using the built-in rate limiter, partitioned per
  caller, rather than a hand-rolled counter.
  USE FOR: throttling requests so one user, tenant, or API key cannot starve the rest; registering
  AddRateLimiter with a partitioned policy keyed on a claim/API key/IP; choosing a limiter algorithm
  (fixed window, sliding window, token bucket, concurrency); returning 429 with Retry-After; applying a
  limiter globally or per route with RequireRateLimiting.
  DO NOT USE FOR: authentication/authorization (use authorize-api-endpoints); output/response caching
  (use output caching); retrying outbound calls (that is client resilience); general middleware ordering.
license: MIT
---

# Rate Limiting

Throttle with the framework's built-in rate limiter, and partition the limit by the caller so one caller cannot consume everyone else's capacity. A hand-rolled counter in custom middleware is not thread-safe across the algorithms you actually want, misses queuing and replenishment, and does not integrate with the pipeline.

## Register a partitioned limiter

`AddRateLimiter` with a policy that partitions on the authenticated caller, so each caller gets its own bucket. Choose an algorithm deliberately.

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("per-tenant", httpContext =>
    {
        var tenant = httpContext.User.FindFirstValue("tid") ?? "anonymous";
        return RateLimitPartition.GetTokenBucketLimiter(tenant, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 100,
            TokensPerPeriod = 100,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
        }

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(new { error = "Too many requests." }, cancellationToken);
    };
});

var app = builder.Build();

app.UseRateLimiter(); // after routing, before the endpoints run
```

## Partition on who is being limited

Key the partition on the identity you are protecting the service from: the authenticated user or tenant claim, an API key, or the client IP as a fallback for anonymous traffic. A single un-partitioned limit throttles the whole service together, so one heavy caller still starves the others.

## Apply the policy

Apply per route or group, or set a global limiter.

```csharp
app.MapGroup("/orders").RequireRateLimiting("per-tenant");
// or, for everything: options.GlobalLimiter = PartitionedRateLimiter.Create(...);
```

## Choose the algorithm deliberately

| Algorithm | Use when |
| --- | --- |
| Token bucket | A per-caller quota that tolerates short bursts up to a cap, then refills steadily. A good default. |
| Fixed window | Simplest; accept the burst at window boundaries. |
| Sliding window | Smoother than fixed window near boundaries. |
| Concurrency | Limit simultaneous in-flight requests rather than the arrival rate. |

## Verify

- Throttling uses `AddRateLimiter` + `UseRateLimiter`, not a hand-rolled counter or bespoke middleware.
- The limit is partitioned per caller (a claim, API key, or IP), not one global bucket for everyone.
- Exceeding the limit returns `429 Too Many Requests`, ideally with a `Retry-After` header.
- `UseRateLimiter` is registered after routing, and the policy is applied to the endpoints (globally or via `RequireRateLimiting`).

❌ A `static ConcurrentDictionary<string,int>` request counter in custom middleware.
✅ `AddRateLimiter` with a partitioned limiter.

❌ One limit for the whole service, so a single tenant exhausts it for everyone.
✅ Partition the limiter on the caller's identity.
