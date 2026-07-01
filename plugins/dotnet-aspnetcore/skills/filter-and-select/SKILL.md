---
name: filter-and-select
description: >
  Add safe, bounded filtering and field selection (sparse fieldsets) to a collection endpoint - the
  small, opt-in subset of OData-style $filter/$select, not a general query language.
  USE FOR: letting clients narrow a list by field values (status=active, price[gte]=100) and choose which
  fields come back (select=id,name) so responses are smaller; translating those query parameters into a
  safe EF Core Where + Select over an allow-list; rejecting unknown fields/operators; keeping the query
  database-side and the ordering/paging stable when only some fields are selected.
  DO NOT USE FOR: forward pagination and the Link header (use controller-data-access / minimal-api-data-access);
  incremental change tracking / delta (use change-tracking-delta); a full query language with OR, nesting,
  joins, or arbitrary expressions; request-body validation (use model-payloads / MVC model binding).
license: MIT
---

# Filtering and Field Selection

Clients want two bounded query capabilities over a collection: **filter** it to the rows they care about, and **select** only the fields they need so responses are small. Both are easy to do dangerously - a dynamic-LINQ string, an `IQueryable` built from raw client input, or in-memory filtering after loading the whole table. The rule for both is the same: **opt-in allow-list**. Only fields you explicitly register are filterable, sortable, or selectable; anything else is a `400`. This bounds the SQL the client can cause, prevents column probing and injection, and keeps the surface a *feature*, not a query engine.

Filtering itself is usually the easy half; the parts that get dropped are **field selection bounded by the allow-list and projected server-side**, **keeping paging stable when the client selects a subset**, and **guarding against expensive queries**. Get those right.

## Declare the allow-list

One registry per resource decides what is filterable/selectable and, per field, its type and permitted operators. Everything downstream reads from it; nothing touches a field that is not here.

```csharp
enum Op { Eq, Ne, Gt, Ge, Lt, Le, Contains, StartsWith }

sealed record FilterField(
    string Name,
    Op[] Ops,
    Func<Op, string, Result<Expression<Func<Product, bool>>>> Build); // coercion returns 400, never throws

static class ProductQuery
{
    // The only field names the client can use; the database projection is bounded to this set.
    public static readonly string[] Selectable = ["id", "name", "category", "price", "createdAt"];

    public static readonly Dictionary<string, FilterField> Filterable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["status"]   = new("status",   [Op.Eq, Op.Ne],                             EnumField(p => p.Status)),
        ["price"]    = new("price",    [Op.Eq, Op.Ne, Op.Gt, Op.Ge, Op.Lt, Op.Le], NumberField(p => p.Price)),
        ["category"] = new("category", [Op.Eq, Op.StartsWith],                     TextField(p => p.Category)),
        // Contains is deliberately excluded from category - see the amplification guardrails below.
    };

    // Coercion returns a Result: an operand that will not parse to the field's type is a 400, never an exception.
    static Func<Op, string, Result<Expression<Func<Product, bool>>>> NumberField(
        Expression<Func<Product, decimal>> selector)
    {
        return (op, raw) =>
        {
            if (!decimal.TryParse(raw, out var value))
            {
                return Result.BadRequest($"'{raw}' is not a valid number.");
            }
            return Result.Ok(Compare(selector, value, op)); // Compare builds the typed >, >=, == expression
        };
    }
    // EnumField and TextField follow the same shape: TryParse the operand, then return BadRequest or the built expression.
}
```

Each entry builds a **strongly-typed** `Expression<Func<Product,bool>>` - EF Core translates it to SQL. There is no `System.Linq.Dynamic`, no string-concatenated predicate, and no `IQueryable` assembled from the raw field name. The coercion (`decimal.TryParse` and its enum/string peers) is the **type check**: a value that will not parse to the field's type comes back as a `400` before any query runs, never a runtime exception mid-query.

## Parse the query string against the allow-list

Read `field=value` as equality and `field[op]=value` (Stripe-style bracket operators) as a comparison. Reject unknown fields and unsupported operators before touching the database.

```csharp
static Result<List<Expression<Func<Product, bool>>>> ParseFilters(IQueryCollection q)
{
    var predicates = new List<Expression<Func<Product, bool>>>();
    foreach (var (rawKey, values) in q)
    {
        if (rawKey is "select" or "top" or "cursor")
        {
            continue; // reserved query keys, handled elsewhere
        }

        var (name, op) = SplitBracket(rawKey);                 // "price[gte]" -> ("price","gte")
        if (!ProductQuery.Filterable.TryGetValue(name, out var field))
        {
            return Result.BadRequest($"Unknown filter field '{name}'.");
        }
        if (!TryParseOp(op, out var parsedOp) || !field.Ops.Contains(parsedOp))
        {
            return Result.BadRequest($"Operator '{op}' is not allowed on '{name}'.");
        }
        if (predicates.Count >= MaxPredicates)                 // amplification cap
        {
            return Result.BadRequest($"At most {MaxPredicates} filters are allowed.");
        }

        var built = field.Build(parsedOp, values[^1]!);        // coercion returns a Result, never throws
        if (!built.Ok)
        {
            return Result.BadRequest(built.Error);
        }
        predicates.Add(built.Value);
    }
    return Result.Ok(predicates);
}
```

## Apply filter, then order, then page, then project

The pipeline order is fixed, and **field selection must not remove the ordering key from the query**:

```csharp
IQueryable<Product> query = db.Products.AsNoTracking();
foreach (var predicate in filters)
{
    query = query.Where(predicate);                                 // AND-combined, all DB-side
}

query = query.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id);           // stable total order
query = ApplyKeyset(query, cursor).Take(pageSize);                    // bounded page (see data-access skill)

// Selection: project SERVER-SIDE to the selectable columns, ALWAYS including id + the sort key,
// so the keyset cursor can still be built even if the client did not select createdAt/id.
var rows = await query
    .Select(p => new ProductRow(p.Id, p.Name, p.Category, p.Price, p.CreatedAt))
    .ToListAsync(ct);

// Shape to the client's requested subset for the wire (id/createdAt retained internally for the cursor).
var selected = ParseSelect(http.Request.Query["select"]);            // validated allow-list subset
var body = rows.Select(r => Shape(r, selected)).ToList();
```

`ParseSelect` splits the comma list and rejects any name not in `Selectable`, compared case-insensitively (`OrdinalIgnoreCase`), with a `400`. The query projects the bounded **selectable** column set server-side - never the whole entity graph - and `Shape` trims each row to the requested subset for the wire; `Id`/`CreatedAt` stay in the query regardless of `select`, so the next-page cursor is always computable. To push the exact per-request subset down to SQL so unselected columns are not even read, build the `Select` as a member-init expression from the requested fields plus the forced key columns; the fixed projection here is the simpler default and reads only the allow-listed columns either way.

## Guard against amplification

An allow-list bounds *which* columns are touched; these bound *how hard* a request can hit the database:

- **Cap the number of predicates** (`MaxPredicates`, e.g. 8) and the **page size** - a filtered result is still a bounded page, never an unbounded `ToList()`.
- **Gate expensive operators.** `StartsWith` is sargable (uses an index on the column); `Contains` compiles to a leading-wildcard `LIKE '%x%'` that **cannot use an index** and scans the table - only allow `Contains` on a field you are willing to full-scan, and prefer `StartsWith`. The registry above allows `StartsWith` but not `Contains` on `category` for exactly this reason.
- **Index every filterable/sortable field**, or a `price[gte]` filter degrades to a scan under load.
- **Coerce and validate operands** to the field's CLR type up front, so a bad value is a `400`, not a query-time failure or an accidental full scan.

## Standards basis

This is the constrained subset of common conventions: bracket operators on a field (`price[gte]=`, as Stripe uses), a flat `select=a,b` sparse fieldset (OData `$select` without the `$`, Google's `fields`). It is deliberately **not** a query language: AND-only (no `OR`), scalar fields only (no nesting, joins, or related-entity paths), one value per operator. Keep it here; graduating to arbitrary expressions reintroduces the injection and cost problems the allow-list exists to prevent.

## Verify

- Filtering and selection are driven by an **explicit allow-list**; an unknown field or a disallowed operator returns `400`, never a silent ignore or a blind pass-through to the query.
- Predicates are **strongly-typed `Expression`s translated to SQL** (EF `Where`), with operands coerced to the field's type - no dynamic-LINQ, no string-built queries, no in-memory filtering of the full table.
- `select` is validated against the allow-list (unknown fields rejected); the query projects the bounded selectable column set server-side (not the whole entity), and each row is trimmed to the requested subset for the response.
- The **ordering/keyset key stays in the query even when not selected**, so paging remains stable and the cursor is still computable.
- Amplification is bounded: predicate count and page size are capped, `Contains`/leading-wildcard is gated in favor of `StartsWith`, and filterable columns are indexed.

❌ `db.Products.Where("Category == \"" + input + "\"")` (dynamic string), or loading all rows then filtering/selecting in memory, or dropping `id`/`createdAt` from the projection so the next-page cursor breaks.
✅ An allow-list of typed, operator-scoped predicates translated to SQL, a validated server-side `select` projection that retains the sort key, and capped predicate/result sizes.
