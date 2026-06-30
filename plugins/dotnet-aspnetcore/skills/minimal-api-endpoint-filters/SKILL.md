---
name: minimal-api-endpoint-filters
description: >
  Apply cross-cutting concerns (request validation, required headers, auditing) to ASP.NET Core minimal
  API endpoints as endpoint filters on a route group, so the rule is written once and every endpoint
  inherits it instead of duplicating it in each handler.
  USE FOR: validating bound request models across many endpoints; enforcing a required header or
  precondition uniformly; short-circuiting with a 400 problem result before the handler runs; writing an
  IEndpointFilter or AddEndpointFilter lambda; controlling filter order; choosing group-level versus
  per-endpoint filters.
  DO NOT USE FOR: controller action filters / MVC filter pipeline; binding custom parameter types (use
  minimal-api-parameter-binding); endpoint result types in general (use author-minimal-api-endpoints);
  authorization rules (use authorize-api-endpoints).
license: MIT
---

# Minimal API Endpoint Filters

Put cross-cutting concerns in an endpoint filter attached to a route group, so the rule lives in one place and every endpoint in the group inherits it. The handler stays focused on its own work and never repeats the check.

## A validation filter on the group

An `IEndpointFilter` inspects the bound arguments and short-circuits by returning a result instead of calling `next` when the request is invalid.

```csharp
public sealed class ValidateFilter<T> : IEndpointFilter where T : IValidatableRequest
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var model = context.Arguments.OfType<T>().FirstOrDefault();
        if (model is null || !model.TryValidate(out var errors))
        {
            return TypedResults.ValidationProblem(errors ?? new Dictionary<string, string[]>());
        }

        return await next(context);
    }
}

var articles = app.MapGroup("/articles")
    .AddEndpointFilter<ValidateFilter<ArticleRequest>>();
```

Returning a result short-circuits the request; calling `next(context)` continues to the next filter or the handler.

## A required-header filter

```csharp
articles.AddEndpointFilter(async (context, next) =>
{
    var version = context.HttpContext.Request.Headers["x-api-version"].ToString();
    if (string.IsNullOrEmpty(version))
    {
        return TypedResults.Problem(
            title: "The x-api-version header is required.",
            statusCode: StatusCodes.Status400BadRequest);
    }

    return await next(context);
});
```

Read bound arguments with `context.GetArgument<T>(index)` or `context.Arguments`; reach the raw request through `context.HttpContext`.

## Order is intentional

Filters run in the order they are added, outermost first: the first `AddEndpointFilter` wraps the second, which wraps the handler, and they unwind in reverse on the way out. Put broad gates (a required header, a precondition) before fine-grained model validation.

## Group versus endpoint

Attach a filter to a `MapGroup` to cover every endpoint in the group; attach it to a single `Map` call to scope it to one endpoint. A group filter is what keeps a cross-cutting rule in one place instead of copied into each handler.

## Verify

- Cross-cutting checks are endpoint filters on the group, not duplicated inside each handler.
- A failed check short-circuits with a 400 (`ValidationProblem` or `Problem`) and does not call `next`.
- The handler body contains no copy of the validation or header logic the filter already performs.
- Filter order is deliberate: broad gates before specific validation.

❌ Repeating the same blank-field checks and header lookups at the top of every handler.
✅ One `IEndpointFilter` (or `AddEndpointFilter` lambda) on the group.

❌ Detecting a bad request but still calling `next`, so the handler runs on invalid input.
✅ Return the problem result to short-circuit before the handler.
