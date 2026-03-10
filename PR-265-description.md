## Summary
Adds the **implementing-server-sent-events** skill for building SSE endpoints in ASP.NET Core 8 minimal APIs.

> **Note:** Replaces #139 (migrated from skills-old repo). Skill moved to `aspnetcore` plugin per repo restructuring.

## What the Skill Teaches
- Manual SSE protocol implementation (no built-in `MapSSE` — doesn't exist)
- Required headers: `Content-Type: text/event-stream`, `Cache-Control: no-cache`, `Connection: keep-alive`
- Correct SSE event framing with `\n\n` terminators
- `Response.Body.FlushAsync()` for immediate delivery
- Event IDs + `Last-Event-ID` header for reconnection support
- `retry:` field for controlling client reconnection interval
- `X-Accel-Buffering: no` for reverse proxy compatibility
- `HttpContext.RequestAborted` / `CancellationToken` for client disconnect detection
- `Channel<T>` for background service → endpoint communication

## Eval Scenarios
1. Implement SSE notification endpoint in ASP.NET Core 8 minimal API

## Files
- `plugins/aspnetcore/plugin.json` — ASP.NET Core plugin
- `plugins/aspnetcore/skills/implementing-server-sent-events/SKILL.md` — skill instructions
- `tests/aspnetcore/implementing-server-sent-events/eval.yaml` — 1 eval scenario (needs expansion)
