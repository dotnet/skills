---
name: minimal-api-parameter-binding
description: >
  Bind custom and value types directly from the route or query in ASP.NET Core minimal API handlers, so
  parsing and validation happen at the boundary and invalid input becomes a 400 before the handler runs.
  USE FOR: taking a strongly typed value (an id wrapper, a code, a date range, coordinates) as a minimal
  API handler parameter instead of a raw string; adding a static TryParse for a single route/query value;
  adding a static BindAsync for a value that spans several inputs or needs HttpContext; rejecting an
  invalid value with 400 at binding time.
  DO NOT USE FOR: controller model binding and [FromQuery]/[FromRoute] (controllers bind differently);
  request body DTO shapes (use the model-payloads skills); cross-cutting validation across many endpoints
  (use minimal-api-endpoint-filters); endpoint result types in general (use author-minimal-api-endpoints).
license: MIT
---

# Minimal API Parameter Binding

Give a custom or value type its own parser so a minimal API binds it straight from the route or query. Parsing and validation then live on the type and run at the boundary: the handler receives a ready, valid value, and bad input becomes a 400 before the handler body executes.

## A single route or query value: static TryParse

When the value comes from one string (a route segment or a single query value), add a static `TryParse`. Minimal APIs discover it and bind automatically; a value that fails to parse is rejected as 400 without reaching the handler.

```csharp
public readonly record struct Sku
{
    private Sku(string value) => Value = value;

    public string Value { get; }

    public static bool TryParse(string? value, IFormatProvider? provider, out Sku result)
    {
        if (!string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, "^[A-Z]{3}-[0-9]{4}$"))
        {
            result = new Sku(value);
            return true;
        }

        result = default;
        return false;
    }
}

catalog.MapGet("/items/{sku}", (Sku sku, CatalogDb db) => /* sku is always valid here */);
// GET /items/not-a-sku  ->  400, the handler never runs.
```

Implement the `(string?, IFormatProvider?, out T)` overload (the `IParsable<T>` shape); minimal APIs also accept the simpler `(string?, out T)`.

## A value spanning several inputs: static BindAsync

When the value needs more than one string (several query keys, a header, or `HttpContext`), add a static `BindAsync`.

```csharp
public readonly record struct DateRange(DateOnly Start, DateOnly End)
{
    public static ValueTask<DateRange?> BindAsync(HttpContext context, ParameterInfo parameter)
    {
        var query = context.Request.Query;
        if (DateOnly.TryParse(query["from"], out var from)
            && DateOnly.TryParse(query["to"], out var to)
            && from <= to)
        {
            return ValueTask.FromResult<DateRange?>(new DateRange(from, to));
        }

        return ValueTask.FromResult<DateRange?>(null);
    }
}

reports.MapGet("/sales", (DateRange range, CatalogDb db) => /* range is valid */);
```

With the non-nullable parameter above, returning `null` from `BindAsync` is automatically a 400 (the value counts as not provided) and the handler does not run. Declare the parameter nullable (`DateRange? range`) only when you want to handle the missing-or-invalid case yourself, then check for `null` and return 400; or throw `BadHttpRequestException` from `BindAsync` to force a 400 regardless of nullability.

## Keep parsing on the type

The handler signature names the typed value; the parse-and-validate rule lives on the type's `TryParse`/`BindAsync`, written once and reused by every endpoint that takes the type. The handler never pokes at a raw string.

## Verify

- The custom type appears directly in the handler signature; the handler does not re-parse a raw string.
- An invalid value yields a 400 before the handler runs (`TryParse` returns `false`, or `BindAsync` returns `null` / throws `BadHttpRequestException`).
- `TryParse` is used for a single route/query value; `BindAsync` when the value spans several inputs or needs `HttpContext`.

❌ Taking `string sku` and validating it with a regex inside every handler.
✅ A `Sku` type with a static `TryParse`, bound at the boundary.

❌ Returning 500, or silently treating an unparseable value as an empty filter.
✅ An invalid value becomes a 400 at binding time.
