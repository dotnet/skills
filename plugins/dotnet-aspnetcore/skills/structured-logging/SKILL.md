---
name: structured-logging
description: >
  Write diagnosable structured logs in ASP.NET Core: message templates with named placeholders, a scope
  to correlate an operation's lines, appropriate levels, and no sensitive data.
  USE FOR: adding logging to an operation so it can be searched and correlated in production; using
  ILogger message templates with named properties instead of interpolated strings; correlating log lines
  for one request/operation with BeginScope or a correlation id; choosing log levels; logging exceptions;
  keeping secrets and PII out of logs; LoggerMessage source generation on hot paths.
  DO NOT USE FOR: distributed tracing/metrics wiring (that is OpenTelemetry); global exception-to-response
  mapping (use IExceptionHandler); audit persistence in the database.
license: MIT
---

# Structured Logging

Log so the values you will search on are captured as **named properties**, and so every line from one operation can be pulled together. A log line is data, not a sentence.

## Message templates with named placeholders, not interpolation

Pass the values as arguments to a template. Each `{Name}` becomes a structured property the log store can index and query; interpolation throws that away and leaves only flat text.

```csharp
logger.LogInformation("Created order {OrderId} for customer {CustomerId}", order.Id, customerId);
```

❌ `logger.LogInformation($"Created order {order.Id} for customer {customerId}");` - interpolated: no `OrderId`/`CustomerId` properties, just a string you cannot filter on.

## Correlate an operation's lines with a scope

A scope attaches the same properties to every entry written inside it, so all lines for one request, order, or tenant can be retrieved together.

```csharp
using (logger.BeginScope(new Dictionary<string, object>
{
    ["OrderId"] = order.Id,
    ["TenantId"] = tenantId
}))
{
    logger.LogInformation("Validating order");
    logger.LogInformation("Charging payment");
    // both lines carry OrderId and TenantId
}
```

The framework already attaches request-level correlation (the trace identifier) to logs; add a scope for the identifiers that matter to *your* operation.

## Levels, exceptions, and content

- Use levels by severity: `Information` for normal flow, `Warning` for recoverable problems, `Error` for failures. Do not log everything at one level.
- Log an exception by passing it as the **first argument**, not by string-formatting it: `logger.LogError(ex, "Failed to create order {OrderId}", order.Id);`.
- Never log secrets, tokens, full PII, or request/response bodies. Log identifiers and outcomes, not payloads.
- On hot paths, use the `LoggerMessage` source generator (a `partial` method annotated `[LoggerMessage(Level = ..., Message = "...")]`) to avoid boxing/allocation and check the template at compile time.

## Verify

- Log calls use message templates with named placeholders; the searchable values (ids, tenant, operation) are structured properties, not interpolated or concatenated into the message text.
- Lines belonging to one operation are correlated with a scope (`BeginScope`) or a correlation identifier.
- Levels are used by severity; exceptions are passed as the exception argument, not formatted into the message.
- No secrets, tokens, PII, or bodies are written to the log.

❌ `logger.LogInformation("Order " + order.Id + " failed: " + ex.Message);` - a flat string, wrong level, and the exception is stringified.
✅ `logger.LogError(ex, "Order {OrderId} failed", order.Id);` inside a scope carrying the operation's identifiers.
