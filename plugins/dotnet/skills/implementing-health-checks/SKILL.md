---
name: implementing-health-checks
description: Implement ASP.NET Core health checks with liveness, readiness, and startup probes for Kubernetes and load balancer integration. Use when configuring health endpoints, monitoring dependencies, or setting up container orchestration probes.
---

# Implementing Health Checks

## When to Use

- Adding health check endpoints to an ASP.NET Core app
- Configuring Kubernetes liveness, readiness, and startup probes
- Monitoring database, cache, or external service availability
- Load balancer health endpoint configuration

## When Not to Use

- The app is not ASP.NET Core
- The user wants application performance monitoring (use OpenTelemetry instead)
- The user needs business-level monitoring (use custom metrics)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| ASP.NET Core project | Yes | The project to add health checks to |
| Dependencies to monitor | No | Database, Redis, message queue, etc. |

## Workflow

### Step 1: Add the health checks packages

```bash
dotnet add package AspNetCore.HealthChecks.SqlServer    # for SQL Server
dotnet add package AspNetCore.HealthChecks.Redis         # for Redis
dotnet add package AspNetCore.HealthChecks.Uris          # for HTTP dependencies
```

> `Microsoft.Extensions.Diagnostics.HealthChecks` is already included in the ASP.NET Core framework — no explicit install needed.

### Step 2: Register health checks with SEPARATE liveness and readiness

**Critical distinction** most implementations get wrong:

- **Liveness** = "Is the process alive?" — Only checks the process isn't deadlocked. Failure → Kubernetes RESTARTS the pod.
- **Readiness** = "Can the process serve traffic?" — Checks dependencies. Failure → Kubernetes STOPS SENDING traffic (but doesn't restart).
- **Startup** = "Has the initial startup completed?" — One-time check. Failure during grace period is expected.

```csharp
builder.Services.AddHealthChecks()
    // Liveness checks: ONLY check the process itself, NEVER external dependencies
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })

    // Readiness checks: check external dependencies
    .AddSqlServer(
        connectionString: builder.Configuration.GetConnectionString("Default")!,
        name: "database",
        tags: new[] { "ready" },
        timeout: TimeSpan.FromSeconds(5))
    .AddRedis(
        redisConnectionString: builder.Configuration.GetConnectionString("Redis")!,
        name: "redis",
        tags: new[] { "ready" },
        timeout: TimeSpan.FromSeconds(5))
    .AddUrlGroup(
        new Uri("https://api.external-service.com/health"),
        name: "external-api",
        tags: new[] { "ready" },
        timeout: TimeSpan.FromSeconds(5));
```

### Step 3: Map separate health endpoints

```csharp
// Liveness: Kubernetes livenessProbe hits this
app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = WriteMinimalResponse
});

// Readiness: Kubernetes readinessProbe hits this
app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteDetailedResponse
});

// Startup: Kubernetes startupProbe hits this
app.MapHealthChecks("/healthz/startup", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = WriteMinimalResponse
});
```

### Step 4: Write response formatters

```csharp
static Task WriteMinimalResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var result = new { status = report.Status.ToString() };
    return context.Response.WriteAsJsonAsync(result);
}

static Task WriteDetailedResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var result = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description,
            duration = e.Value.Duration.TotalMilliseconds
        })
    };
    return context.Response.WriteAsJsonAsync(result);
}
```

### Step 5: Configure Kubernetes probes

```yaml
# In the Kubernetes deployment spec:
containers:
  - name: myapp
    livenessProbe:
      httpGet:
        path: /healthz/live
        port: 8080
      initialDelaySeconds: 0    # Start checking immediately
      periodSeconds: 10
      failureThreshold: 3       # Restart after 3 failures
    readinessProbe:
      httpGet:
        path: /healthz/ready
        port: 8080
      initialDelaySeconds: 5
      periodSeconds: 10
      failureThreshold: 3       # Stop traffic after 3 failures
    startupProbe:
      httpGet:
        path: /healthz/startup
        port: 8080
      initialDelaySeconds: 0
      periodSeconds: 5
      failureThreshold: 30      # Allow up to 150s for startup
```

### Step 6: Add health check UI (optional)

```bash
dotnet add package AspNetCore.HealthChecks.UI
dotnet add package AspNetCore.HealthChecks.UI.InMemory.Storage
```

```csharp
builder.Services.AddHealthChecksUI().AddInMemoryStorage();
app.MapHealthChecksUI();
```

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Checking DB in liveness probe | DB down → pod restarts → makes outage worse. DB checks go in READINESS only |
| No timeout on health checks | Add `timeout: TimeSpan.FromSeconds(5)` to each check registration |
| Health endpoint not excluded from auth | Add `.AllowAnonymous()` to `MapHealthChecks` or exclude path in auth middleware |
| Startup probe missing | Without it, liveness probe kills pods during slow cold starts |
| All checks on one endpoint | Separate live/ready/startup — mixing them causes cascading restarts |
