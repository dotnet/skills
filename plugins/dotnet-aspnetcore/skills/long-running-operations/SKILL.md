---
name: long-running-operations
description: >
  Model slow API work as an asynchronous operation: accept the request, return 202 with a pollable
  operation-status resource, and let the client poll until a terminal state, instead of blocking the
  request until the work finishes.
  USE FOR: an endpoint whose work takes too long to finish within a request (provisioning, generating a
  report, a large import); returning 202 Accepted with a Location to an operation resource; modeling an
  operation with its own id and a status lifecycle (running, succeeded, failed); a status endpoint the
  client polls; Retry-After hints; pointing at the finished resource on success.
  DO NOT USE FOR: fast synchronous endpoints (return the result directly); background jobs with no client
  waiting on them; streaming responses; endpoint result types in general (use author-controller-endpoints).
license: MIT
---

# Long-Running Operations

When an operation cannot finish quickly within the request, do not hold the connection until it is done. Accept the request, start the work, and return **202 Accepted** with a pointer to a separate **operation** resource the client polls until the operation reaches a terminal state. The slow work becomes a first-class resource with its own identity and lifecycle, not a blocked HTTP call.

## Accept the request and return 202

```csharp
[HttpPost]
[ProducesResponseType(StatusCodes.Status202Accepted)]
public async Task<IActionResult> StartExport(CreateExportRequest request, CancellationToken ct)
{
    var operation = new Operation
    {
        Id = Guid.NewGuid(),
        Status = OperationStatus.Running,
        CreatedAt = DateTimeOffset.UtcNow
    };
    db.Operations.Add(operation);
    await db.SaveChangesAsync(ct);

    await queue.EnqueueAsync(operation.Id, request, ct); // hand the work to a background worker

    Response.Headers.Location = Url.Link(nameof(GetOperation), new { id = operation.Id });
    Response.Headers.RetryAfter = "5"; // suggested poll interval, in seconds
    return Accepted();
}
```

The work runs outside the request (for example a `BackgroundService` draining a queue), which updates the operation's status to `Succeeded` or `Failed` when it finishes.

## Expose a distinct operation-status resource

The client polls this resource, which is separate from the resource being produced.

```csharp
[HttpGet("/operations/{id:guid}", Name = nameof(GetOperation))]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<ActionResult<OperationDto>> GetOperation(Guid id, CancellationToken ct)
{
    var operation = await db.Operations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
    if (operation is null)
    {
        return NotFound();
    }

    if (operation.Status == OperationStatus.Succeeded)
    {
        Response.Headers.Location = operation.ResultUrl; // where the finished resource lives
    }

    return Ok(operation.ToDto()); // carries Status: Running / Succeeded / Failed (+ error on failure)
}
```

## Model the states

An operation has its own id and a status with a clear **terminal** set: `Running` while in progress, then `Succeeded` or `Failed` (with an error). The client polls until the status is terminal and then stops; on success it follows the pointer to the finished resource. Keep the operation record after completion so a late poll still returns the outcome.

## Verify

- A slow create returns **202 Accepted** promptly with a `Location` to an operation resource, rather than doing all the work in the request and returning 200/201 at the end.
- A distinct operation-status endpoint exists and is what the client polls, separate from the resource being produced.
- The status distinguishes `Running` from the terminal `Succeeded`/`Failed`, so the client knows when to stop polling.
- On success the operation points at the finished resource; a `Retry-After` hints the poll interval.

❌ Doing the whole slow job inside the request handler and returning 200 once it eventually finishes.
✅ Return 202, run the work in the background, and expose an operation resource to poll.

❌ Reporting progress only through fields on the target resource, with no operation to poll.
✅ A dedicated operation resource with its own id and a running/succeeded/failed lifecycle.
