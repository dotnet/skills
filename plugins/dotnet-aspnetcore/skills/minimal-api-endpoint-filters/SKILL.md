---
name: minimal-api-endpoint-filters
description: >
  Use ASP.NET Core minimal API endpoint filters for cross-cutting concerns that need the bound (already
  validated) request arguments or the handler's result, and route everything else to the right built-in
  mechanism instead.
  USE FOR: rewriting or normalizing a bound argument before the handler runs; transforming or shaping the
  result uniformly across a route group (response envelopes, field selection); short-circuiting based on
  the bound arguments; choosing group-level versus per-endpoint filters; filter ordering.
  DO NOT USE FOR: input/model validation (use built-in minimal API validation, AddValidation with
  DataAnnotations / IValidatableObject); concerns that need neither the bound arguments nor the typed
  result, such as logging, CORS, or header forwarding (use middleware); authentication/authorization (use
  authorize-api-endpoints); turning exceptions into ProblemDetails (use IExceptionHandler); caching a
  response by key (use output caching); the MVC/controller filter pipeline.
license: MIT
---

# Minimal API Endpoint Filters

An endpoint filter runs after model binding and validation and wraps the handler, so it can read and **rewrite the bound, already-validated arguments** before the handler runs, and inspect and **change the result** the handler returned. That access is the whole reason to use one. Reach for a filter only when both are true:

1. **The concern needs the bound arguments or the result.** A concern that touches neither (logging, CORS, a raw header) belongs in middleware, not a filter.
2. **No built-in mechanism already covers the concern with the same access.** Input validation, for example, *does* need the bound arguments, yet built-in minimal API validation (`AddValidation()` with DataAnnotations / `IValidatableObject`) already covers it thoroughly, so a validation filter only duplicates it.

When a concern clears both tests, the filter is doing something nothing else in the pipeline can. The two examples below qualify because each needs argument or result access that no built-in mechanism provides; see [When not to use a filter](#when-not-to-use-a-filter) for the common cases that fail one of the tests.

## Rewrite a bound argument before the handler

Because the filter runs after binding and validation, it receives the strongly typed, valid arguments and can replace one before the handler sees it. This is the place to canonicalize an input across every endpoint in a group.

```csharp
var articles = app.MapGroup("/articles");

articles.AddEndpointFilter(async (context, next) =>
{
    if (context.GetArgument<ArticleRequest>(0) is { } request)
    {
        // Canonicalize the bound argument; the handler and everything after it see the normalized value.
        context.Arguments[0] = request with { Slug = request.Slug.Trim().ToLowerInvariant() };
    }

    return await next(context);
});
```

`context.Arguments` is the mutable list of bound arguments; `context.GetArgument<T>(index)` reads one by position. Returning a result instead of calling `next(context)` short-circuits the request.

## Change the result the handler returned

A filter can call `next`, then act on what the handler produced. Here a field-selection filter uses the request's `fields` value together with the returned object to send back only the requested properties, so every endpoint in the group supports partial responses without each handler knowing about it.

```csharp
public sealed class FieldSelectionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);

        var fields = context.HttpContext.Request.Query["fields"].ToString();
        if (string.IsNullOrEmpty(fields) || result is IResult)
        {
            return result; // nothing requested, or a status/problem result: leave it untouched
        }

        var selected = fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (JsonSerializer.SerializeToNode(result) is not JsonObject shaped)
        {
            return result;
        }

        foreach (var name in shaped.Select(property => property.Key).ToList())
        {
            if (!selected.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                shaped.Remove(name);
            }
        }

        return shaped;
    }
}

articles.AddEndpointFilter<FieldSelectionFilter>();
```

The handler returns its domain value; the filter reshapes it. Middleware cannot do this, because it sees the serialized byte stream rather than the typed result the handler returned.

## When not to use a filter

Each concern below fails one of the two tests: it either does not need the bound arguments or the result (so it belongs in middleware), or it needs them but a built-in mechanism already covers it with the same access (so a filter would only duplicate that mechanism). Validation is the one to watch, because it *looks* like a filter job (it needs the bound arguments) yet built-in validation already has the same access and does it thoroughly.

| Concern | Fails test | Use instead of a filter |
| --- | --- | --- |
| Input / model validation (required fields, ranges, formats) | 2: needs the args, but already covered with equal access | Built-in minimal API validation: `AddValidation()` with DataAnnotations or `IValidatableObject` returns a 400 `ValidationProblem` automatically |
| Logging, CORS, header forwarding, response compression, anything on the raw request/response | 1: needs neither the bound arguments nor the typed result | Middleware |
| Requiring a signed-in user, roles, policies, resource rules | 2: already covered by the authorization system | `RequireAuthorization` / policies (see the authorization skill) |
| Turning an exception into a consistent error response | 2: already covered | `IExceptionHandler` + `AddProblemDetails` |
| Returning a stored response for a repeated request | 2: already covered | Output caching (`AddOutputCache`) |

## Order is intentional

Filters run in the order they are added, outermost first: the first `AddEndpointFilter` wraps the second, which wraps the handler, and they unwind in reverse on the way out. Put a filter that rewrites arguments before one that depends on the rewritten value; put result-shaping filters where they see the final result.

## Group versus endpoint

Attach a filter to a `MapGroup` to cover every endpoint in the group; attach it to a single `Map` call to scope it to one endpoint. A group filter is what keeps a cross-cutting concern in one place instead of copied into each handler.

## Verify

- The filter genuinely needs the bound arguments or the result; a concern that needs neither uses middleware, and input validation uses built-in minimal API validation rather than a filter.
- An argument-rewriting filter mutates `context.Arguments` (or returns a short-circuit result) before calling `next`; the handler observes the rewritten value.
- A result-shaping filter calls `next`, then inspects or replaces the returned value, and leaves problem/status results untouched.
- Cross-cutting filters are attached at the group level, not duplicated inside each handler.
- Filter order is deliberate when one filter depends on another's effect.

❌ A hand-written filter that checks required fields and returns 400.
✅ Built-in validation (`AddValidation()` + DataAnnotations / `IValidatableObject`).

❌ A filter that only logs the request or rewrites a raw header and never touches the bound arguments or the result.
✅ Middleware for concerns that need neither the bound arguments nor the typed result.
