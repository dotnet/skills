# PR Feedback Summary

Compiled: March 4, 2026

---

## PR #155 — Add minimal-api-file-upload skill (open)
**13 review threads** — Reviewers: **copilot-pull-request-reviewer**, **timheuer**

| Reviewer | Feedback |
|----------|----------|
| copilot | Step 1 is contradictory about whether `IFormFile` requires `[FromForm]` vs being auto-bound in .NET 8 |
| copilot | "Safe filename" still derives extension from user-controlled `file.FileName` — prefer deriving from validated magic bytes |
| copilot | `context.Request.GetMultipartBoundary()` isn't a built-in ASP.NET Core API — include the helper or use standard parsing |
| copilot | Eval rubric allows validating only `ContentType` but PR description emphasizes magic-byte validation — tighten rubric |
| copilot | Global Kestrel limit set to 100MB but scenario enforces 10MB — confusing |
| copilot | `ReadAsync` return value is ignored when reading magic bytes — can misclassify empty/short files |
| copilot | `image/gif` in allowed MIME types but scenario is JPEG+PNG only |
| copilot | "IFormFile buffers the entire file in memory" is inaccurate — ASP.NET Core spills to temp file |
| copilot | `GetContentDispositionHeader()` / `IsFileDisposition()` aren't built-in APIs |
| **timheuer** | Name too verbose — suggest "minimal-api-file-upload" |
| **timheuer** | Any validation that "8" (in .NET 8) is going to influence too much? |
| **timheuer** | Strike "8" and put more information in 'when to use' |
| **timheuer** | Typo: "endpoings" → "endpoints" |

---

## PR #147 — Add implementing-server-sent-events skill (open)
**13 review threads** — Reviewers: **copilot-pull-request-reviewer**, **danmoseley**, **BrennanConroy**

| Reviewer | Feedback |
|----------|----------|
| copilot | `context.Response.Headers.ContentType` / `.CacheControl` won't compile — use `context.Response.ContentType` / `Headers["Cache-Control"]` |
| copilot | `StreamWriter.WriteLineAsync` uses platform newlines — prefer explicit `\n` for SSE |
| copilot | `int.Parse(lastEventId)` will throw on non-integer — use `int.TryParse` |
| copilot | Missing "Validation" section per CONTRIBUTING.md |
| copilot | Eval file placed under `src/dotnet/tests/` instead of `tests/<plugin>/<skill>/` |
| copilot | SKILL.md under `src/dotnet/skills/` instead of `plugins/<plugin>/skills/` |
| **danmoseley** | Move when-to-use/not-use into description to enable lazy loading |
| **danmoseley** | `{message}` should be sanitized for newlines — injection risk |
| **danmoseley** | Add note about connection limits to prevent resource exhaustion |
| **danmoseley** | Add note about CORS configuration for cross-origin EventSource |
| **BrennanConroy** | **Wrong.** ASP.NET Core 10 has `TypedResults.ServerSentEvents` — most of the skill should be rewritten to use it |

---

## PR #146 — Add implementing-json-patch-aspnetcore skill (open)
**4 review threads** — Reviewer: **copilot-pull-request-reviewer**

| Feedback |
|----------|
| Minimal API example `patchDoc.ApplyTo(dto)` omits error tracking — should use `ModelStateDictionary` or catch `JsonPatchException` |
| `validationResults.ToDictionary(r => r.MemberNames.First(), ...)` can throw on object-level validation with no member names |
| "Restrict patchable properties" example silently removes disallowed ops — better to reject with 400 |
| Step 1 claims `AddNewtonsoftJson()` is "REQUIRED" but minimal API bypasses MVC formatters. Soften the claim or show reuse of configured settings |

---

## PR #142 — Add implementing-websocket-endpoints skill (open)
**11 review threads** — Reviewers: **copilot-pull-request-reviewer**, **BrennanConroy**

| Reviewer | Feedback |
|----------|----------|
| copilot | Entire file incorrectly wrapped in `` ```skill `` code block |
| copilot | Duplicate `UseWebSockets` call (line 71 and 83) |
| copilot | Logic inconsistency with `EndOfMessage` — comment says "Don't process partial messages!" but processes anyway |
| copilot | eval.yaml has leading spaces before `scenarios:` |
| copilot | Comment about `AddWebSockets` not existing is inaccurate |
| copilot | Comment says `ToList()` snapshot but code doesn't call `ToList()` |
| copilot | Browser WebSocket API doesn't support custom headers **at all**, not just "after initial handshake" |
| copilot | `access_token` in query string risks leaking via logs/Referer headers |
| **BrennanConroy** | Consider setting `KeepAliveTimeout` as well |
| **BrennanConroy** | What about binary message handling (`else binary??`) |

---

## PR #131 — Add implementing-rate-limiting skill (open)
**13 review threads** — Reviewers: **copilot-pull-request-reviewer**, **danmoseley**, **BrennanConroy**

| Reviewer | Feedback |
|----------|----------|
| copilot | File wrapped in `` ```skill `` code block — frontmatter won't parse |
| copilot | Warns about fixed window burst problem then immediately configures global limiter as fixed window |
| copilot | Missing `using System.Security.Claims;` for `ClaimTypes.NameIdentifier` |
| copilot | Health-check assertion too permissive — just mentioning `healthz` passes without actually disabling rate limiting |
| **danmoseley** | Move when-to-use/not-use into description for lazy loading |
| **danmoseley** | Add CODEOWNERS entry |
| **danmoseley** | Remove `` ```skill `` markdown wrapper |
| **danmoseley** | Missing `using System.Security.Claims;` |
| **danmoseley** | Is `RejectionStatusCode = 429` line needed since it's the default? |
| **danmoseley** | Need more than 1 eval scenario to cover skill breadth |
| **danmoseley** | Add more keywords in description to improve activation |
| **BrennanConroy** | Move `UseRateLimiter()` after `UseAuthorization()` so you can rate-limit based on user info |
| **BrennanConroy** | Code uses `RemoteIpAddress` inconsistently |

---

## PR #92 — Add securing-aspnetcore-apis skill (closed/merged)
**6 review threads** — Reviewer: **BrennanConroy**

| Feedback |
|----------|
| Mention WebSocket origin checks (link to docs) |
| "Not applicable" section is too broad — some concepts still apply |
| Add insecure CORS pattern: `policy.SetIsOriginAllowed(origin => return true)` |
| Consider different rate limit partitions for anonymous vs. authenticated users |
| Assume more eval scenarios will be added in the future? |
| Reference the official middleware order docs page instead of maintaining an explicit list |

---

## PR #91 — Add configuring-opentelemetry-dotnet skill (open)
**18 review threads** — Reviewers: **copilot-pull-request-reviewer**, **tarekgh**, **noahfalk**

| Reviewer | Feedback |
|----------|----------|
| copilot | Missing `using OpenTelemetry.Trace;` for `SetStatus()`/`RecordException()` |
| copilot | File wrapped in `` ```skill `` code block — metadata won't parse |
| copilot | `return order;` references undefined variable |
| **tarekgh** | Does `SetDbStatementForText` exist in latest SqlClient instrumentation? |
| **tarekgh** | Does it need to reference `OpenTelemetry.Instrumentation.Runtime` package? |
| **tarekgh** | Traces don't configure endpoint but metrics do? |
| **tarekgh** | Missing `using` directives |
| **tarekgh** | Is `GetQueueDepth` just demonstrating the idea? |
| **tarekgh** | Is HttpClient instrumentation accurate for clients not from `IHttpClientFactory`? |
| **noahfalk** | Fix package list (suggestion provided) |
| **noahfalk** | Fix description (suggestion provided) |
| **noahfalk** | SQL instrumentation should be clearly marked **optional** |
| **noahfalk** | Runtime metrics should be marked **optional** |
| **noahfalk** | Custom tracing spans should also be optional (but more useful) |
| **noahfalk** | Use `IMeterFactory` instead of static `Meter` per official guidance |
| **noahfalk** | What about logs/metrics verification? |
| **noahfalk** | `IMeterFactory` again for custom metrics section |
| **noahfalk** | Eval prompts should be simpler/more generalized (e.g. "Please enable telemetry for my app") |

---

## PR #90 — Add optimizing-ef-core-queries skill (closed/merged)
**9 review threads** — Reviewers: **copilot-pull-request-reviewer**, **AndriySvyryd**, **roji**

| Reviewer | Feedback |
|----------|----------|
| copilot | Duplicate regex pattern `N\\+1` in eval.yaml |
| copilot | Capitalize "Cartesian" (proper noun) — multiple instances |
| **AndriySvyryd** | `EnableSensitiveDataLogging()` and `EnableDetailedErrors()` not useful for perf issues |
| **AndriySvyryd** | N+1 example overstated — only affects lazy-loading apps; look for lazy-loading in general |
| **roji** | *Agrees* — lazy loading should be discouraged generally (sync-only I/O is bad for scalability) |
| **AndriySvyryd** | Compiled queries only matter for complex queries |
| **roji** | *Agrees* — discourage compiled queries unless confirmed measured impact; leave out of generic skill |
| **AndriySvyryd** | "Global query filters applied to wrong entity" is not a perf issue |
| **AndriySvyryd**/**roji** | Connection resilience should be combined with **DbContext pooling** — needs its own section |

---

## PR #89 — Add migrating-newtonsoft-to-system-text-json skill (open)
**11 review threads** — Reviewer: **copilot-pull-request-reviewer**

| Feedback |
|----------|
| **Incorrect claim**: STJ "throws by default (.NET 8+)" for extra JSON properties — STJ ignores them by default |
| **Incorrect claim**: Null to non-nullable value type behavior difference is misleading — both libraries default to `default(T)` |
| "Newtonsoft default" comment on `PropertyNameCaseInsensitive` is wrong — both libraries are case-sensitive by default |
| **Incorrect claim**: Newtonsoft uses "camelCase by default" — it uses PascalCase (property names as-is) |
| Eval rubric propagates incorrect case-insensitivity claim |
| Skill uses `` ```skill `` code fence instead of `---` YAML frontmatter |
| Multiple instances of incorrect "Newtonsoft default" behavior |
| Eval prompt asks to "match Newtonsoft.Json's default behavior" based on incorrect assumptions |
| Rubric expects "default casing" warning but both libraries share the same default |
| Pitfall "Forgetting PropertyNameCaseInsensitive = true" based on incorrect premise |
| "Newtonsoft default" comment on camelCase is incorrect — requires explicit configuration |

---

## PR #88 — Add implementing-health-checks skill (open)
**3 review threads** — Reviewer: **copilot-pull-request-reviewer**

| Feedback |
|----------|
| Startup probe uses `Predicate = _ => true` — runs all checks including DB/Redis; should use `"live"` tag only |
| Step 2 health check registrations don't include timeout parameters despite Common Pitfalls recommending it |
| `Microsoft.Extensions.Diagnostics.HealthChecks` is already in the framework — explicit install is redundant |

---

## Cross-cutting Themes

1. **Formatting**: Multiple PRs use `` ```skill `` wrapper instead of `---` YAML frontmatter (#131, #142, #89, #91)
2. **File placement**: Skills should be under `plugins/` and evals under `tests/`, not `src/dotnet/` (#147)
3. **Missing `using` directives**: Common across several skills (#91, #131)
4. **When-to-use in description**: Move this content into the description field for lazy-loading activation (#147, #131)
5. **Factual accuracy**: PR #89 has multiple incorrect claims about Newtonsoft.Json defaults
6. **API correctness**: PR #147 needs rewrite for `TypedResults.ServerSentEvents` in .NET 10
