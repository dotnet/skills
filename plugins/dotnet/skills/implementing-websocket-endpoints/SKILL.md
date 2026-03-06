```skill
---
name: implementing-websocket-endpoints
description: Implement WebSocket endpoints in ASP.NET Core 8+ using the built-in middleware. Use when adding real-time bidirectional communication to an API.
---

# Implementing WebSocket Endpoints in ASP.NET Core

## When to Use

- Real-time bidirectional communication (chat, live updates, gaming)
- Need to push data from server to client without polling
- Lower overhead than HTTP long-polling or SSE for high-frequency updates
- Client needs to send messages to server unprompted

## When Not to Use

- Server-to-client only (use Server-Sent Events — simpler)
- Need automatic reconnection and hub abstraction (use SignalR instead)
- Simple request/response patterns (use regular HTTP)

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
// Program.cs
builder.Services.AddWebSockets(options =>
{
    // WRONG — this method doesn't exist! Use raw middleware options:
});

// CORRECT:
var app = builder.Build();

app.UseWebSockets(new WebSocketOptions
{
    // CRITICAL: KeepAliveInterval sends ping frames to keep connection alive
    // Default is 2 minutes. Set to match your infrastructure timeouts.
    KeepAliveInterval = TimeSpan.FromSeconds(30),

    // Allowed origins (for browser CORS protection)
    // CRITICAL: Without this, ANY website can open a WebSocket to your API
    AllowedOrigins = { "https://myapp.com", "https://www.myapp.com" }
});

// CRITICAL ORDERING: UseWebSockets MUST come before the endpoint that handles WebSockets
app.UseWebSockets();    // ← BEFORE
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
            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

            // CRITICAL: For large messages, EndOfMessage may be false
            // You must accumulate fragments until EndOfMessage == true
            if (!result.EndOfMessage)
            {
                // Accumulate into a MemoryStream or larger buffer
                // Don't process partial messages!
            }

            // Echo back (or process the message)
            var responseBytes = Encoding.UTF8.GetBytes($"Echo: {message}");
            await webSocket.SendAsync(
                new ArraySegment<byte>(responseBytes),
                WebSocketMessageType.Text,
                endOfMessage: true,  // ← MUST set this for the last (or only) fragment
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
            .Where(s => s.State == WebSocketState.Open)  // Only open sockets
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
// CRITICAL: WebSocket connections don't support standard HTTP auth headers
// after the initial handshake. The auth happens on the HTTP upgrade request.

// Option 1: Query string token (common for browser clients)
app.Map("/ws", async (HttpContext context) =>
{
    // Browser WebSocket API doesn't support custom headers
    // Use query string: ws://server/ws?access_token=xxx
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

// Option 2: Cookie auth works naturally (cookies are sent on upgrade request)
// Option 3: Use [Authorize] attribute if using cookie or negotiate auth
```

## Common Mistakes

1. **Looking for `MapWebSocket()`**: This method doesn't exist. WebSocket handling uses `UseWebSockets()` middleware + manual upgrade via `context.WebSockets.AcceptWebSocketAsync()`.

2. **Using `CloseAsync` instead of `CloseOutputAsync`**: When the client initiates close, respond with `CloseOutputAsync`. `CloseAsync` initiates a NEW close handshake (deadlock risk if both sides use it).

3. **Not checking `EndOfMessage`**: Large messages may arrive in fragments. Process only when `EndOfMessage == true`.

4. **Missing `AllowedOrigins`**: Without origin checking, any website can connect to your WebSocket endpoint (cross-site WebSocket hijacking).

5. **Forgetting `KeepAliveInterval`**: Load balancers and proxies close idle connections. The default 2 minutes may be too long — set to 30 seconds.

6. **Not handling concurrent broadcasts safely**: Use `ConcurrentDictionary` and snapshot collections before iteration.
```
