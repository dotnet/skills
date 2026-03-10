## Summary
Adds the **implementing-websocket-endpoints** skill for building WebSocket endpoints in ASP.NET Core 8.

> **Note:** Replaces #142 (migrated from skills-old repo). Skill moved to `aspnetcore` plugin per repo restructuring.

## What the Skill Teaches
- `UseWebSockets()` middleware setup (no `MapWebSocket` — doesn't exist)
- Manual WebSocket upgrade via `AcceptWebSocketAsync`
- Proper receive loop with `EndOfMessage` fragment handling
- **Critical:** Use `CloseOutputAsync` (not `CloseAsync`) when responding to client-initiated close to avoid deadlock
- `ConcurrentDictionary`-based connection manager for broadcast
- `KeepAliveInterval` and `KeepAliveTimeout` configuration
- `AllowedOrigins` for cross-origin protection
- Query string token authentication (browser WebSocket API cannot send custom headers)

## Eval Scenarios
1. Implement WebSocket chat endpoint in ASP.NET Core 8
2. Fix WebSocket CloseAsync deadlock
3. WebSocket should not be used for server-to-client streaming (negative test)
4. Handle fragmented WebSocket messages correctly

## Files
- `plugins/aspnetcore/plugin.json` — ASP.NET Core plugin
- `plugins/aspnetcore/skills/implementing-websocket-endpoints/SKILL.md` — skill instructions
- `tests/aspnetcore/implementing-websocket-endpoints/eval.yaml` — 4 eval scenarios
