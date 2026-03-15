## Summary
Adds the **implementing-rate-limiting** skill for ASP.NET Core's built-in rate limiting middleware (.NET 7+).

> **Note:** Replaces #131 (migrated from skills-old repo). Skill moved to `aspnetcore` plugin per repo restructuring.

## What the Skill Teaches
- Algorithm selection: fixed window vs sliding window vs token bucket vs concurrency limiter
- **Critical:** Setting `RejectionStatusCode = 429` (default is 503!)
- Per-IP and per-user partitioned rate limiting
- Middleware pipeline ordering (`UseRateLimiter()` after `UseRouting()`)
- `OnRejected` callback with `Retry-After` header
- `DisableRateLimiting()` for health check endpoints
- Named policies with `RequireRateLimiting`

## Eval Results (3-run, `claude-opus-4.6`)

| Scenario | Baseline | With Skill | Verdict |
|----------|----------|------------|---------|
| Add per-client rate limiting to an ASP.NET Core API | 4.0/5 | **4.7/5** | ❌ [1] |
| Rate limiting silently inactive without UseRateLimiter | 4.3/5 | 2.7/5 | ❌ |
| Fix rate limiter returning 503 instead of 429 | 2.3/5 | 2.3/5 | ❌ [2] |
| Rate limiting should not apply to authentication scenarios | 4.0/5 | **4.7/5** | ✅ |
| Choose correct rate limiting algorithm | 4.0/5 | **5.0/5** | ✅ |

[1] Quality improved but weighted score penalized by token/tool/time overhead.
[2] Timeouts impacted scoring. Will iterate with increased timeouts.

## Files
- `plugins/aspnetcore/plugin.json` — new ASP.NET Core plugin
- `plugins/aspnetcore/skills/implementing-rate-limiting/SKILL.md` — skill instructions
- `tests/aspnetcore/implementing-rate-limiting/eval.yaml` — 5 eval scenarios
