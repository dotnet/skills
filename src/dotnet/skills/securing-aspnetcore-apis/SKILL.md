---
name: securing-aspnetcore-apis
description: Secure ASP.NET Core APIs with authentication, authorization, JWT bearer tokens, CORS configuration, and rate limiting. Use when adding security to web APIs, configuring auth middleware, or fixing common security misconfigurations.
---

# Securing ASP.NET Core APIs

## When to Use

- Adding authentication/authorization to an ASP.NET Core API
- Configuring JWT bearer token validation
- Setting up CORS policies for browser clients
- Implementing rate limiting to prevent abuse
- Fixing security misconfigurations

## When Not to Use

- The user is building a server-rendered MVC app with cookie auth (different patterns)
- The app is internal-only behind a service mesh that handles auth
- The user needs OAuth provider setup (IdP-specific, not general .NET)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| ASP.NET Core project | Yes | The API project to secure |
| Auth requirements | No | JWT, API key, OAuth, or mixed |

## Workflow

### Step 1: Add JWT Bearer authentication

```bash
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
```

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://login.microsoftonline.com/{tenant-id}/v2.0";
        options.Audience = "api://{client-id}";

        // CRITICAL: Do NOT disable these in production
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(5)  // default; resist reducing to 0
        };
    });

builder.Services.AddAuthorization();
```

### Step 2: Configure middleware in the CORRECT order

**Middleware order is critical. Wrong order = auth bypassed silently.**

```csharp
var app = builder.Build();

// 1. Exception handling first (catches errors from all middleware)
app.UseExceptionHandler("/error");

// 2. HTTPS redirection
app.UseHttpsRedirection();

// 3. CORS — MUST be before auth for preflight requests to work
app.UseCors();

// 4. Authentication — MUST be before Authorization
app.UseAuthentication();

// 5. Authorization — MUST be after Authentication
app.UseAuthorization();

// 6. Rate limiting — after auth so you can rate-limit per user
app.UseRateLimiter();

// 7. Endpoints
app.MapControllers();
```

**Common mistake:** Putting `UseAuthorization()` before `UseAuthentication()` — auth checks run but identity is never set, so everything returns 401.

### Step 3: Apply authorization policies

**Per-endpoint (Minimal APIs):**
```csharp
app.MapGet("/api/orders", GetOrders)
    .RequireAuthorization();

app.MapDelete("/api/orders/{id}", DeleteOrder)
    .RequireAuthorization("AdminOnly");
```

**Policy-based authorization:**
```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"));

    options.AddPolicy("CanManageOrders", policy =>
        policy.RequireClaim("permission", "orders.write"));

    // Fallback policy — applies to ALL endpoints without explicit auth
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
```

**IMPORTANT:** Setting `FallbackPolicy` makes ALL endpoints require auth by default. Explicitly allow anonymous where needed:

```csharp
app.MapGet("/health", () => "OK").AllowAnonymous();
app.MapPost("/api/auth/login", Login).AllowAnonymous();
```

### Step 4: Configure CORS correctly

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("Production", policy =>
    {
        policy.WithOrigins(
                "https://app.example.com",
                "https://admin.example.com")
            .WithMethods("GET", "POST", "PUT", "DELETE")
            .WithHeaders("Authorization", "Content-Type")
            .AllowCredentials();  // Required if frontend sends cookies/tokens
    });
});

// Apply globally
app.UseCors("Production");
```

**NEVER do this in production:**
```csharp
// INSECURE — allows any origin to call your API
policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
```

### Step 5: Add rate limiting (.NET 7+)

```csharp
builder.Services.AddRateLimiter(options =>
{
    // Global limiter
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        context => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User?.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
```

### Step 6: Security headers

```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "0");  // Modern browsers don't need it
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Content-Security-Policy", "default-src 'self'");
    await next();
});
```

## Security Checklist

- [ ] `UseAuthentication()` comes BEFORE `UseAuthorization()`
- [ ] JWT validation checks issuer, audience, lifetime, and signing key
- [ ] CORS `WithOrigins` lists specific origins (not `AllowAnyOrigin`)
- [ ] All endpoints require auth by default (FallbackPolicy)
- [ ] Health/login endpoints explicitly marked `AllowAnonymous`
- [ ] Rate limiting enabled with per-user partitioning
- [ ] HTTPS enforced with `UseHttpsRedirection`
- [ ] No secrets in `appsettings.json` (use user-secrets or Key Vault)

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Auth middleware order wrong | Auth → Authz, always in that order. CORS before both |
| `AllowAnyOrigin` in production | Whitelist specific origins |
| JWT secret in appsettings.json | Use environment variables, user-secrets, or Azure Key Vault |
| 401 instead of 403 | 401 = not authenticated; 403 = authenticated but not authorized. Check claims |
| CORS preflight failures | Browser sends OPTIONS; ensure CORS middleware handles it before auth |
| Rate limiter not per-user | Partition by user identity OR IP, not globally |
