---
name: structure-api-business-logic
description: "Structure ASP.NET Core API business logic: move operation logic into a service layer and report outcomes with a hand-rolled Result type that the endpoint translates to HTTP. USE FOR: extracting route-handler or controller-action logic into a service / application class; deciding how a service reports success versus failure; introducing a Result<T> with a semantic error kind instead of throwing exceptions or scattering error returns; mapping a service outcome to HTTP status codes; keeping DbContext and business rules out of endpoints; reusing one operation across several endpoints. DO NOT USE FOR: the HTTP mechanics of a single endpoint's return type (use author-minimal-api-endpoints or author-controller-endpoints); DTO shapes and entity mapping details (use the model-payloads skills); EF Core query or pagination specifics (use the data-access skills); deciding which operations or resources to expose (use design-api-operations)."
license: MIT
---

# Structure API Business Logic

Keep route handlers thin: move an operation's logic into a service that returns a result, and let the endpoint translate that result into HTTP.

## Move the operation into a service

When an endpoint does more than a trivial single-entity read or write (it validates input, touches several entities, enforces a business rule, or shares logic with another endpoint), put that logic in a service or application class. The endpoint binds the request, calls the service, and translates the outcome; it does not use `DbContext` directly inside a route-handler lambda or controller action once a service exists.

Trivial CRUD over a single entity can stay inline. The threshold is real work: validation, multiple entities, rules, or reuse.

## Report outcomes with a hand-rolled Result

The service returns a `Result<T>` that is either a success carrying the value or a failure carrying a semantic `ErrorType` (for example `Validation`, `NotFound`, `Conflict`). Do not throw exceptions for these expected outcomes, do not return bare tuples, and never return `IResult`, `Results<...>`, or `TypedResults` from the service — those belong to the endpoint.

Ship a minimal, dependency-free result type:

```csharp
public enum ErrorType { Validation, NotFound, Conflict }

public sealed record Error(ErrorType Type, string Code, string Message);

public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public Error? Error { get; }

    private Result(T value) { IsSuccess = true; Value = value; Error = null; }
    private Result(Error error) { IsSuccess = false; Value = default; Error = error; }

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(Error error) => new(error);
}
```

The service receives a request DTO or command (never an Entity Framework entity) and maps it to entities with hand-written code:

```csharp
public sealed class OrderService(StoreDbContext db)
{
    public async Task<Result<Order>> PlaceOrderAsync(PlaceOrderCommand cmd, CancellationToken ct)
    {
        if (cmd.Items.Count == 0)
            return Result<Order>.Failure(new(ErrorType.Validation, "items.empty", "At least one item is required."));

        if (!await db.Customers.AnyAsync(c => c.Id == cmd.CustomerId, ct))
            return Result<Order>.Failure(new(ErrorType.NotFound, "customer.notFound", "Customer not found."));

        var order = new Order { CustomerId = cmd.CustomerId, CreatedAt = DateTimeOffset.UtcNow, Status = OrderStatus.Pending };
        // ...validate products, capture server-side prices, build line items...
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return Result<Order>.Success(order);
    }
}
```

## Translate the Result at the endpoint

The endpoint is the only place that knows HTTP. It maps each `ErrorType` to the matching status and the success to the right 2xx.

```csharp
orders.MapPost("/", async Task<Results<CreatedAtRoute<Order>, NotFound, Conflict, ValidationProblem>> (
    PlaceOrderRequest req, OrderService service, CancellationToken ct) =>
{
    var result = await service.PlaceOrderAsync(req.ToCommand(), ct);
    return result.IsSuccess
        ? TypedResults.CreatedAtRoute(result.Value!, "GetOrder", new { id = result.Value!.Id })
        : result.Error!.Type switch
        {
            ErrorType.NotFound => TypedResults.NotFound(),
            ErrorType.Conflict => TypedResults.Conflict(),
            _ => TypedResults.ValidationProblem(new Dictionary<string, string[]> { [result.Error.Code] = [result.Error.Message] }),
        };
});
```

Map `Validation` to 400 ProblemDetails, `NotFound` to 404, `Conflict` to 409. A missing customer or product is `NotFound` (404), not a validation error (400).

## Reuse the operation, don't duplicate it

Logic needed by more than one endpoint lives once in the service. Each endpoint calls the same method and translates its `Result`; never copy validation or creation rules into two handlers.

## Verify

- No `DbContext` usage inside route-handler lambdas or controller actions for non-trivial operations.
- The service returns `Result<T>` (not tuples, exceptions, or ASP.NET result types) and takes a DTO/command, not an entity.
- Each `ErrorType` maps to a distinct status: `Validation` to 400, `NotFound` to 404, `Conflict` to 409.
- `dotnet build` succeeds (run `dotnet restore` first if needed).
