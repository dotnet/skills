---
name: authorize-api-endpoints
description: >
  Authorize ASP.NET Core API endpoints (controllers and minimal APIs) with the authorization framework
  instead of inline checks, choosing declarative or imperative by where the decision data lives.
  USE FOR: enforcing who may call an endpoint or act on a resource; role/claim rules as named policies;
  resource ownership or tenant/membership rules; writing IAuthorizationRequirement + AuthorizationHandler;
  reading the route, endpoint, and endpoint metadata from context.Resource (the HttpContext) inside a
  handler; deciding between [Authorize(Policy=...)] / RequireAuthorization and an imperative
  IAuthorizationService.AuthorizeAsync; returning 401 vs 403 vs 404; wiring authentication/authorization
  and middleware order.
  DO NOT USE FOR: authenticating users or issuing tokens/login UI; endpoint result types and status codes
  in general (use author-controller-endpoints / author-minimal-api-endpoints); service-layer structure
  (use structure-api-business-logic).
license: MIT
---

# Authorize API Endpoints

Express every access rule through the authorization framework (requirements, handlers, and named policies), not as inline `if (User...)` checks scattered through actions. The one design decision that matters: **where does the data the rule needs live?**

- **In the request and the caller's claims** (a route value, a header, a role, a tenant claim): decide it **declaratively**. Write a policy backed by a requirement and handler; the handler reads `context.Resource` as the `HttpContext` to reach the route and the endpoint's metadata. No database load.
- **In the stored entity** (a field such as `OwnerId` you only know after loading the row): decide it **imperatively**. Load the entity, then call `IAuthorizationService.AuthorizeAsync(User, entity, policy)` and translate the result.

Most rules are the first kind. Reach for the second only when the decision genuinely needs persisted state.

## Declarative: a handler that reads HttpContext and endpoint metadata

In endpoint routing, `AuthorizationHandlerContext.Resource` is the `HttpContext`. From it the handler reaches the route values and the endpoint, including custom metadata you attach to the endpoint. Carrying the "what to check" as endpoint metadata keeps one handler reusable across endpoints with different route shapes.

```csharp
// Metadata attached to an endpoint describes how this resource is addressed.
public sealed class OrgRouteMetadata(string routeKey)
{
    public string RouteKey { get; } = routeKey; // e.g. "orgId"
}

public sealed class SameOrgRequirement : IAuthorizationRequirement;

public sealed class SameOrgHandler : AuthorizationHandler<SameOrgRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SameOrgRequirement requirement)
    {
        // Endpoint-routing authorization passes the HttpContext as the resource.
        if (context.Resource is not HttpContext http)
        {
            return Task.CompletedTask;
        }

        var endpoint = http.GetEndpoint();
        var route = endpoint?.Metadata.GetMetadata<OrgRouteMetadata>();
        if (route is null)
        {
            return Task.CompletedTask;
        }

        var routeOrg = http.Request.RouteValues[route.RouteKey] as string;
        var userOrg = context.User.FindFirstValue("org");

        // Succeed when the rule is met; admins are allowed regardless.
        if (context.User.IsInRole("admin")
            || (routeOrg is not null && string.Equals(routeOrg, userOrg, StringComparison.Ordinal)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask; // never Fail: see OR semantics below
    }
}
```

Register the handler and expose the requirement as a named policy, then opt endpoints in and attach the metadata:

```csharp
builder.Services.AddSingleton<IAuthorizationHandler, SameOrgHandler>();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("same-org", policy => policy.Requirements.Add(new SameOrgRequirement()));

// Controller: [Authorize(Policy = "same-org")] on the action/controller, plus the metadata via an attribute,
// or, minimal API:
var projects = app.MapGroup("/orgs/{orgId}/projects")
    .RequireAuthorization("same-org")
    .WithMetadata(new OrgRouteMetadata("orgId"));
```

### OR semantics: Succeed, never Fail

A handler calls `context.Succeed(requirement)` when its rule is met and otherwise simply returns. Do **not** call `context.Fail()` for an unmet rule: a requirement can be satisfied by any one of several handlers (for example a tenant-match handler and an admin-allowance handler), and `Fail()` vetoes all of them. Absence of `Succeed` already denies by default.

## Imperative: when the rule needs the loaded entity

Ownership lives in the row, not the route, so the decision can only be made after loading. Inject `IAuthorizationService`, author a resource-typed handler, and authorize the loaded entity.

```csharp
public sealed class OwnerOrAdminRequirement : IAuthorizationRequirement;

public sealed class OwnerOrAdminHandler : AuthorizationHandler<OwnerOrAdminRequirement, Project>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, OwnerOrAdminRequirement requirement, Project resource)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (context.User.IsInRole("admin") || string.Equals(resource.OwnerId, userId, StringComparison.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

// In the action / handler:
var project = await db.Projects.FindAsync([id], ct);
if (project is null)
{
    return TypedResults.NotFound(); // 404: resource does not exist
}

var authz = await authorizationService.AuthorizeAsync(User, project, "owner-or-admin");
if (!authz.Succeeded)
{
    return TypedResults.Forbid(); // 403: exists, but caller may not
}
```

Check existence first so a missing resource is **404** and an existing-but-forbidden one is **403**; an unauthenticated caller is **401** (the framework returns this when no policy is satisfied and no user is present).

## Named policies for role and claim rules

Plain role or claim gates need no handler: declare them as named policies and apply them.

```csharp
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()) // every endpoint requires a signed-in user
    .AddPolicy("admin", policy => policy.RequireRole("admin"))
    .AddPolicy("same-org", policy => policy.Requirements.Add(new SameOrgRequirement()));

// Destructive operations require the admin policy; reads inherit the fallback (authenticated).
adminOnly.MapDelete("/{id}", ...).RequireAuthorization("admin");
```

A **fallback policy** secures every endpoint by default, so a new endpoint is not accidentally left open. Opt specific endpoints out with `[AllowAnonymous]` / `AllowAnonymous()`.

## Wire-up and lifetimes

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddAuthorizationBuilder() /* policies as above */;

app.UseAuthentication();  // establishes who the caller is
app.UseAuthorization();   // enforces the policies; must come after UseAuthentication, after routing
```

Register a handler as a **singleton** when it has no scoped dependencies; register it as **scoped** if it needs a scoped service (for example a `DbContext`) so it is not captured by a singleton.

## Verify

- Access rules are requirements/handlers exposed as named policies, applied with `[Authorize(Policy=...)]` or `.RequireAuthorization(...)`, not inline `if (User...)` checks duplicated per action.
- A route/claim-derivable rule is declarative; its handler reads `context.Resource` as `HttpContext` for route values and endpoint metadata and does not load the entity.
- An ownership/state rule that needs the loaded entity uses `IAuthorizationService.AuthorizeAsync(User, entity, policy)` after loading, mapping to **403** (forbidden) versus **404** (absent).
- Handlers `Succeed` only and never `Fail`, so OR-combined rules and admin allowances still pass.
- A fallback policy requires authentication everywhere; `UseAuthentication` precedes `UseAuthorization`.
- Decisions use the authenticated `User`'s claims, compared with `StringComparison.Ordinal`, never a client-supplied id from the body or query.

❌ `if (User.FindFirstValue("org") != routeOrg) return Forbid();` repeated in every action.
✅ One `SameOrgRequirement` + handler exposed as a `same-org` policy and reused.

❌ Forcing an ownership rule that needs the loaded row into `[Authorize]` before the entity exists.
✅ Load, then `AuthorizeAsync(User, entity, policy)`; 404 before 403.

❌ `context.Fail()` in a handler that is one of several OR alternatives.
✅ `context.Succeed(requirement)` when met; return otherwise.
