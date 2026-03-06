---
name: implementing-server-sent-events
description: Implement Server-Sent Events (SSE) endpoints in ASP.NET Core. Use when building real-time streaming from server to client without WebSockets.
---

# Implementing Server-Sent Events (SSE) in ASP.NET Core

## When to Use
- Server-to-client real-time push (notifications, live updates, streaming progress)
- When you DON'T need bidirectional communication (use WebSockets for that)
- SSE is simpler than WebSockets and works over standard HTTP
- Automatic reconnection built into EventSource browser API

## When Not to Use
- Bidirectional communication needed → use WebSockets
- Binary data streaming → use WebSockets or gRPC streaming
- Need more than 6 concurrent connections per domain in HTTP/1.1 → use HTTP/2

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Event data source | Yes | The data to stream (IAsyncEnumerable, Channel, timer, etc.) |
| Event types | No | Named event types for `event:` field |
| Client reconnection | No | Whether to support Last-Event-ID reconnection |

## Workflow

### Step 1: CRITICAL — There Is No Built-In MapSSE() or MapServerSentEvents()

ASP.NET Core has NO built-in SSE endpoint helper. You must manually write the SSE protocol.

```csharp
// COMMON MISTAKE: Trying to use a non-existent API
// app.MapSSE("/events", ...);                    // DOES NOT EXIST
// app.MapServerSentEvents("/events", ...);       // DOES NOT EXIST
// app.UseServerSentEvents();                     // DOES NOT EXIST

// CORRECT: Use a standard minimal API endpoint with manual SSE protocol
app.MapGet("/events", async (HttpContext context, CancellationToken ct) =>
{
    // CRITICAL: Set these three headers for SSE
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers["Connection"] = "keep-alive";

    // CRITICAL: Disable response buffering for reverse proxies (nginx, etc.)
    context.Response.Headers["X-Accel-Buffering"] = "no";

    await context.Response.Body.FlushAsync(ct);

    // Stream events...
});
```

### Step 2: CRITICAL — SSE Protocol Format

The SSE format has strict rules. Each field ends with `\n`, and each event ends with `\n\n` (double newline).

```csharp
// CRITICAL: The SSE format is NOT just "send text"
// Each event MUST end with TWO newlines (\n\n)

// Simple data event:
await context.Response.WriteAsync($"data: {message}\n\n", ct);
await context.Response.Body.FlushAsync(ct);

// COMMON MISTAKE: Forgetting the double newline
// await context.Response.WriteAsync($"data: {message}\n", ct);  // WRONG - event never completes

// Named event with id (for reconnection):
await context.Response.WriteAsync($"id: {eventId}\n", ct);
await context.Response.WriteAsync($"event: userJoined\n", ct);
await context.Response.WriteAsync($"data: {jsonPayload}\n\n", ct);
await context.Response.Body.FlushAsync(ct);

// Multi-line data (each line needs "data: " prefix):
await context.Response.WriteAsync($"data: line 1\n", ct);
await context.Response.WriteAsync($"data: line 2\n", ct);
await context.Response.WriteAsync($"data: line 3\n\n", ct);  // Only last line gets double \n
await context.Response.Body.FlushAsync(ct);
```

### Step 3: CRITICAL — Flush After Every Event

```csharp
// CRITICAL: You MUST flush after every event, otherwise the client
// won't receive anything until the buffer fills up

// Option A: Flush manually after each event
await context.Response.WriteAsync($"data: {msg}\n\n", ct);
await context.Response.Body.FlushAsync(ct);  // CRITICAL

// Option B: Use StreamWriter with AutoFlush = true
await using var writer = new StreamWriter(context.Response.Body, leaveOpen: true);
writer.AutoFlush = true;  // Flushes after every Write
await writer.WriteLineAsync($"data: {msg}\n");  // Note: WriteLine adds one \n, we add one more

// COMMON MISTAKE: Forgetting FlushAsync — client sees nothing
// await context.Response.WriteAsync($"data: hello\n\n", ct);
// // Missing FlushAsync! Client receives nothing until connection closes.
```

### Step 4: CRITICAL — Handle Client Disconnection with RequestAborted

```csharp
app.MapGet("/events/stream", async (HttpContext context) =>
{
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";

    // CRITICAL: Use RequestAborted to detect client disconnect
    var ct = context.RequestAborted;

    try
    {
        while (!ct.IsCancellationRequested)
        {
            var data = await GetNextEvent(ct);
            await context.Response.WriteAsync($"data: {data}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException)
    {
        // Client disconnected — this is normal, not an error
        // COMMON MISTAKE: Logging this as an error or letting it propagate
    }
});
```

### Step 5: Support Client Reconnection with Last-Event-ID

```csharp
app.MapGet("/events", async (HttpContext context) =>
{
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";

    // CRITICAL: When EventSource reconnects, it sends Last-Event-ID header
    var lastEventId = context.Request.Headers["Last-Event-ID"].FirstOrDefault();

    // Set retry interval (milliseconds) — how long client waits before reconnecting
    await context.Response.WriteAsync($"retry: 5000\n\n");
    await context.Response.Body.FlushAsync();

    var startFrom = lastEventId != null ? int.Parse(lastEventId) + 1 : 0;

    var ct = context.RequestAborted;
    var eventId = startFrom;

    try
    {
        while (!ct.IsCancellationRequested)
        {
            var data = await GetNextEvent(eventId, ct);
            // CRITICAL: Send id: field so client can reconnect from this point
            await context.Response.WriteAsync($"id: {eventId}\ndata: {data}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
            eventId++;
        }
    }
    catch (OperationCanceledException) { }
});
```

### Step 6: Complete Implementation with IAsyncEnumerable

```csharp
app.MapGet("/events/notifications", async (
    HttpContext context,
    INotificationService notifications) =>
{
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers["Connection"] = "keep-alive";
    context.Response.Headers["X-Accel-Buffering"] = "no";

    var ct = context.RequestAborted;
    var eventId = 0;

    try
    {
        await foreach (var notification in notifications.StreamAsync(ct))
        {
            var json = JsonSerializer.Serialize(notification);
            await context.Response.WriteAsync(
                $"id: {eventId++}\nevent: {notification.Type}\ndata: {json}\n\n", ct);
            await context.Response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException) { }

    // CRITICAL: Don't return a value — the response is already written to
    // COMMON MISTAKE: return Results.Ok() after streaming — this corrupts the response
});
```

## Common Mistakes

1. **Using a non-existent MapSSE() or MapServerSentEvents() method**: ASP.NET Core has no built-in SSE helper. You must manually set headers and write the SSE protocol format.
2. **Forgetting double newline**: Events MUST end with `\n\n`. A single `\n` means the event is not complete and the client won't process it.
3. **Not flushing**: Without `FlushAsync()` after each event, the response is buffered and the client receives nothing until disconnect.
4. **Not handling RequestAborted**: The loop runs forever if you don't check `context.RequestAborted`. `OperationCanceledException` on disconnect is normal.
5. **Returning a result after streaming**: Don't `return Results.Ok()` after writing SSE events — the response body is already being written.
6. **Missing Content-Type header**: Must be exactly `text/event-stream`, not `application/json` or `text/plain`.
7. **Missing X-Accel-Buffering: no**: Reverse proxies (nginx) buffer responses by default, breaking SSE.
