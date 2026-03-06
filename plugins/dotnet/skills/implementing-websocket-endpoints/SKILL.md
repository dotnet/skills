---
name: implementing-websocket-endpoints
description: >
  Implement raw WebSocket endpoints in ASP.NET Core 8+ using the built-in middleware.
  USE FOR: real-time bidirectional communication (chat, live updates, gaming), WebSocket
  receive/send loops, AcceptWebSocketAsync, UseWebSockets middleware, connection lifecycle,
  broadcasting to multiple clients, WebSocket authentication.
  DO NOT USE FOR: server-to-client only streaming (use SSE or TypedResults.ServerSentEvents),
  apps needing automatic reconnection and hub abstraction (use SignalR), simple request/response (use HTTP).
---

# Implementing WebSocket Endpoints in ASP.NET Core

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| WebSocket path | Yes | URL path for WebSocket endpoint |
| Message format | No | Text (JSON) or binary |
| Connection management | No | How to track connected clients |

## Workflow

### Step 1: CRITICAL — There is no `MapWebSocket()` method

```csharp
// COMMON MISTAKE: Developers look for a MapWebSocket method.
// It does NOT exist in ASP.NET Core.

// WRONG — these don't exist:
app.MapWebSocket("/ws", handler);     // ❌ NOT a real method
app.MapGet("/ws").UseWebSocket();     // ❌ NOT a real method

// CORRECT — WebSocket is middleware-based, not endpoint-routing:
app.UseWebSockets();  // ← Register the middleware

// Then handle WebSocket requests in regular middleware or endpoints:
app.Map("/ws", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var ws = await context.WebSockets.AcceptWebSocketAsync();
    await HandleWebSocketAsync(ws, context.RequestAborted);
});
```

### Step 2: Configure WebSocket middleware

```csharp
var app = builder.Build();

app.UseWebSockets(new WebSocketOptions
{
    // CRITICAL: KeepAliveInterval sends ping frames to keep connection alive
    // Default is 2 minutes. Set to match your infrastructure timeouts.
    KeepAliveInterval = TimeSpan.FromSeconds(30),

    // KeepAliveTimeout: how long to wait for pong response before closing
    // Set this to detect dead connections faster
    KeepAliveTimeout = TimeSpan.FromSeconds(15),

    // Allowed origins (for browser CORS protection)
    // CRITICAL: Without explicit AllowedOrigins, ANY website can open a WebSocket to your API
    AllowedOrigins = { "https://myapp.com", "https://www.myapp.com" }
});

// CRITICAL ORDERING: UseWebSockets MUST come before the endpoint that handles WebSockets
app.UseRouting();
app.UseAuthorization();
// WebSocket handling endpoint comes after routing
```

### Step 3: Implement the echo/receive loop

```csharp
static async Task HandleWebSocketAsync(WebSocket webSocket, CancellationToken ct)
{
    var buffer = new byte[4096];

    // CRITICAL: The receive loop pattern
    // ReceiveAsync returns when a message (or close) is received
    var result = await webSocket.ReceiveAsync(
        new ArraySegment<byte>(buffer), ct);

    while (!result.CloseStatus.HasValue)
    {
        if (result.MessageType == WebSocketMessageType.Text)
        {
            // CRITICAL: For large messages, EndOfMessage may be false
            // You MUST accumulate fragments until EndOfMessage == true
            if (!result.EndOfMessage)
            {
                // Accumulate into a MemoryStream or larger buffer
                // Do NOT process until EndOfMessage == true
                result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), ct);
                continue;
            }

            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

            // Echo back (or process the message)
            var responseBytes = Encoding.UTF8.GetBytes($"Echo: {message}");
            await webSocket.SendAsync(
                new ArraySegment<byte>(responseBytes),
                WebSocketMessageType.Text,
                endOfMessage: true,  // ← MUST set this for the last (or only) fragment
                ct);
        }
        else if (result.MessageType == WebSocketMessageType.Binary)
        {
            // Handle binary messages — echo back or process as needed
            await webSocket.SendAsync(
                new ArraySegment<byte>(buffer, 0, result.Count),
                WebSocketMessageType.Binary,
                endOfMessage: result.EndOfMessage,
                ct);
        }

        result = await webSocket.ReceiveAsync(
            new ArraySegment<byte>(buffer), ct);
    }

    // CRITICAL: Properly close the WebSocket
    // You MUST respond to a close with CloseOutputAsync, NOT CloseAsync
    // CloseAsync = send close + wait for response (use when YOU initiate close)
    // CloseOutputAsync = respond to close (use when CLIENT initiated close)
    await webSocket.CloseOutputAsync(
        result.CloseStatus.Value,
        result.CloseStatusDescription,
        ct);
}
```

### Step 4: Broadcasting to multiple clients

```csharp
// Thread-safe connection manager
public class WebSocketConnectionManager
{
    // CRITICAL: Use ConcurrentDictionary, not Dictionary
    // Multiple clients connect/disconnect concurrently
    private readonly ConcurrentDictionary<string, WebSocket> _connections = new();

    public string AddConnection(WebSocket socket)
    {
        var id = Guid.NewGuid().ToString("N");
        _connections.TryAdd(id, socket);
        return id;
    }

    public void RemoveConnection(string id)
    {
        _connections.TryRemove(id, out _);
    }

    public async Task BroadcastAsync(string message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        var segment = new ArraySegment<byte>(bytes);

        // CRITICAL: ToList() snapshot to avoid modification during iteration
        var tasks = _connections.Values
            .Where(s => s.State == WebSocketState.Open)
            .ToList()  // ← Snapshot! Without this, concurrent disconnects cause exceptions
            .Select(s => s.SendAsync(segment, WebSocketMessageType.Text, true, ct));

        // CRITICAL: Use Task.WhenAll for parallel sends
        // But handle individual failures — one broken connection shouldn't kill all sends
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception)
        {
            // Individual connections may have closed — clean up in the receive loop
        }
    }
}

// Register as singleton (shared state across all requests):
builder.Services.AddSingleton<WebSocketConnectionManager>();
```

### Step 5: Authentication with WebSockets

```csharp
// CRITICAL: The browser WebSocket API does NOT support custom HTTP headers at all.
// Unlike fetch/XMLHttpRequest, you CANNOT set Authorization headers on a WebSocket connection.
// Auth must happen via query string, cookies, or a pre-auth handshake.

// Option 1: Query string token (common for browser clients)
// ⚠️ SECURITY WARNING: Tokens in query strings may leak via server/proxy logs,
// browser history, and Referer headers. Always use wss:// (TLS) and consider
// short-lived tokens or cookie-based auth for production.
app.Map("/ws", async (HttpContext context) =>
{
    var token = context.Request.Query["access_token"];
    if (string.IsNullOrEmpty(token))
    {
        context.Response.StatusCode = 401;
        return;
    }

    // Validate token here...

    if (context.WebSockets.IsWebSocketRequest)
    {
        using var ws = await context.WebSockets.AcceptWebSocketAsync();
        await HandleWebSocketAsync(ws, context.RequestAborted);
    }
});

// Option 2: Cookie auth works naturally (cookies are sent on the HTTP upgrade request)
// Option 3: Use [Authorize] attribute if using cookie or negotiate auth
```

## Common Mistakes

1. **Looking for `MapWebSocket()`**: This method doesn't exist. WebSocket handling uses `UseWebSockets()` middleware + manual upgrade via `context.WebSockets.AcceptWebSocketAsync()`.

2. **Using `CloseAsync` instead of `CloseOutputAsync`**: When the client initiates close, respond with `CloseOutputAsync`. `CloseAsync` initiates a NEW close handshake (deadlock risk if both sides use it).

3. **Not checking `EndOfMessage`**: Large messages may arrive in fragments. Process only when `EndOfMessage == true`.

4. **Missing `AllowedOrigins`**: Without origin checking, any website can connect to your WebSocket endpoint (cross-site WebSocket hijacking).

5. **Forgetting `KeepAliveInterval`**: Load balancers and proxies close idle connections. The default 2 minutes may be too long — set to 30 seconds.

6. **Not handling concurrent broadcasts safely**: Use `ConcurrentDictionary` and snapshot collections (`.ToList()`) before iteration.
