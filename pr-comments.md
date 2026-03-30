# PR Comments Summary

## Table of Contents
- [PR #264 — Add minimal-api-file-upload skill](#pr-264--add-minimal-api-file-upload-skill)
  - [Old PR #155 (closed)](#old-pr-155-closed--add-minimal-api-file-upload-skill)
- [PR #200 — Add migrating-newtonsoft-to-system-text-json skill](#pr-200--add-migrating-newtonsoft-to-system-text-json-skill)
  - [Old PR #89 (closed)](#old-pr-89-closed--add-migrating-newtonsoft-to-system-text-json-skill)
- [PR #268 — Add configuring-opentelemetry-dotnet skill](#pr-268--add-configuring-opentelemetry-dotnet-skill)
  - [Old PR #91 (closed)](#old-pr-91-closed--add-configuring-opentelemetry-dotnet-skill)
- [PR #269 — Add refactoring-to-async skill](#pr-269--add-refactoring-to-async-skill)
  - [Old PR #79 (closed)](#old-pr-79-closed--add-refactoring-to-async-skill)

---

# PR #264 — Add minimal-api-file-upload skill

**State:** Open | **Author:** mrsharm | **Created:** 2026-03-06 | **Comments:** 23
**Replaces:** #155

## Discussion Comments

### mrsharm (2026-03-06)
> **Migration Note**
>
> This PR replaces #155 which was opened from `mrsharm/skills-old`. The skill and eval files have been migrated to the new `plugins/` directory structure:
>
> - `src/dotnet/skills/minimal-api-file-upload/` → `plugins/dotnet/skills/minimal-api-file-upload/`
> - `src/dotnet/tests/minimal-api-file-upload/` → `tests/dotnet/minimal-api-file-upload/`
>
> All prior review feedback from #155 still applies — please see that PR for the full discussion history.

### mrsharm (2026-03-06)
> **Feedback carried over from #155**
>
> **Copilot** on SKILL.md (line 43): Step 1 is internally contradictory: it's titled as if IFormFile requires [FromForm] (not automatic), but later in this section it states IFormFile *is* bound automatically in .NET 8. Please rewrite this section to present one clear rule.
>
> **Copilot** on SKILL.md (line 138): The "safe filename" still uses `Path.GetExtension(file.FileName)`, which derives the extension from user-controlled input. Prefer choosing the extension based on the validated signature.
>
> **Copilot** on SKILL.md (line 163): `context.Request.GetMultipartBoundary()` isn't a built-in ASP.NET Core API. Please include the helper implementation or switch to the standard boundary parsing approach.
>
> **Copilot** on eval.yaml (line 12): Rubric criterion allows validating only `ContentType`, but the PR description explicitly calls out ContentType as client-spoofable. Tighten this rubric to require signature/magic-byte checking.
>
> **Copilot** on SKILL.md (line 64): These examples set the global Kestrel request body limit to 100MB, which can be confusing given the scenario is about enforcing a 10MB maximum.
>
> **Copilot** on SKILL.md (line 125): When reading magic bytes, the code ignores the return value from ReadAsync. Capture the bytes-read and fail fast if fewer than the required bytes are available.
>
> **Copilot** on SKILL.md (line 118): The allowed MIME types include `image/gif`, but the scenario/rubric for this skill is JPEG + PNG only.
>
> **Copilot** on SKILL.md (line 153): The statement that "IFormFile buffers the entire file in memory by default" is inaccurate for ASP.NET Core. Multipart parsing uses buffering with a memory threshold and typically spills to a temp file.
>
> **Copilot** on SKILL.md (line 172): `section.GetContentDispositionHeader()` / `contentDisposition.IsFileDisposition()` also aren't built-in APIs.
>
> **timheuer**: This feels _too_ verbose a name -- "minimal-api-file-upload"?
>
> **timheuer**: Any validation that "8" is going to influence too much?
>
> **timheuer**: Strike "8" and put more information in the 'when to use'?
>
> **timheuer**: - File upload endpoints in ASP.NET minimal APIs (.NET 8+)
>
> **halter73** on SKILL.md (line 108): @GrabYourPitchforks @blowdart This should be okay for unauthenticated endpoints and endpoints using JWT bearer authentication, but I worry that this might cause people to disable antiforgery for endpoints authenticated with cookies.
>
> **ViktorHofer**: As discussed offline, this PR will need to be re-submitted from a connected fork. Also please update this PR based on the new repo folder structure (plugins instead of src).

### ViktorHofer (2026-03-08)
> Same feedback as in the other PRs regarding the skill not getting activated. If intentional, add `expect_activation: false`

### ManishJayaswal (2026-03-10)
> @mrsharm - the repo has undergone some restructuring to make everything more organized. Hence, we are asking all open PRs to update the branch. Sorry about this. This skill should be under ASP plugin. Please update the PR and submit again.
> @adityamandaleeka @BrennanConroy - please review

### danmoseley (2026-03-24)
> If #207 creates a dotnet-aspnet plugin, this skill should presumably move there.

### danmoseley (2026-03-25)
> [running evaluate just to check the new evaluate improvements...]

### danmoseley (2026-03-25) — Eval Failure Diagnosis
> **Verdict: FAIL** — overall +2.5% improvement (needs ≥10%)
>
> | Scenario | Quality | Score | Status |
> |----------|---------|-------|--------|
> | Implement secure upload | 4.3→5.0 | +15.1% | ✅ |
> | Fix 400 Bad Request | **5.0→4.3** | **-20.7%** | ❌ timeout |
> | Upload multiple + metadata | 3.7→5.0 | +40.5% | ✅ |
>
> **Scenario 2 is sinking the entire eval.** Scenarios 1 & 3 are solid.
>
> **Root Causes (priority order):**
> 1. **Timeout too low (60s)** — fix first. The skilled runs hit the 60s ceiling. **Fix:** `timeout: 60` → `timeout: 180`
> 2. **Baseline already perfect (5.0/5)** — The anti-forgery gotcha is well-documented. No headroom. **Fix options:** Add `reject_tools: ["bash", "edit"]`, or tighten the prompt, or remove the scenario.
> 3. **Rubric item #3 flagged as "technique" by overfitting analysis** — Soften to something like: "Explained why file upload endpoints commonly need antiforgery handling adjusted"
>
> **Recommended Fix Order:**
> 1. Increase timeout for scenario 2: `60` → `180`
> 2. Add `reject_tools: ["bash", "edit"]` to scenario 2
> 3. *(Optional)* Soften rubric item 3

## Review Comments (PR #264)

### copilot-pull-request-reviewer — `plugins/dotnet/skills/minimal-api-file-upload/SKILL.md`
> In the streaming example, `contentDisposition.FileName.Value` is used directly to compute the extension. This value can include paths and other unexpected content; using it directly reintroduces path traversal / spoofing risks. Prefer extracting a safe base name first.

### copilot-pull-request-reviewer — `plugins/dotnet/skills/minimal-api-file-upload/SKILL.md`
> The text claims `IFormFile` "buffers the entire file in memory by default". ASP.NET Core typically buffers form bodies with a memory threshold and spills to disk, so the guidance here is misleading.

### copilot-pull-request-reviewer — `plugins/dotnet/skills/minimal-api-file-upload/SKILL.md`
> The Step 1 heading/example says it's a "COMMON MISTAKE" to expect `IFormFile` to bind automatically, but immediately below it says `IFormFile IS bound automatically from form data in .NET 8`. This is internally inconsistent.

### copilot-pull-request-reviewer — `plugins/dotnet/skills/minimal-api-file-upload/SKILL.md`
> The allowlist includes `image/gif`, but the eval scenario and the surrounding guidance emphasize JPEG/PNG-only uploads.

### copilot-pull-request-reviewer — `plugins/dotnet/skills/minimal-api-file-upload/SKILL.md` L151
> `safeFileName` uses `Path.GetExtension(file.FileName)`, which is attacker-controlled. Prefer deriving the extension from the validated file signature/content type.

### copilot-pull-request-reviewer — `plugins/dotnet/skills/minimal-api-file-upload/SKILL.md`
> This snippet calls `context.Request.GetMultipartBoundary()`, but that isn't a standard ASP.NET Core API.

### copilot-pull-request-reviewer — `plugins/aspnetcore/plugin.json` L5
> Plugin manifests in this repo conventionally use the array form for `skills` (e.g., `"skills": ["./skills/"]`).

### copilot-pull-request-reviewer — `plugins/aspnetcore/plugin.json` L6
> The PR description lists paths under `plugins/dotnet/...` and `tests/dotnet/...`, but this PR adds an `aspnetcore` plugin. Please update the PR description.

### copilot-pull-request-reviewer — `plugins/aspnetcore/skills/minimal-api-file-upload/SKILL.md`
> The markdown contains mojibake/encoding artifacts (e.g., "ΓåÆ", "ΓÇö"). Replace these with proper Unicode characters.

### copilot-pull-request-reviewer — `plugins/aspnetcore/skills/minimal-api-file-upload/SKILL.md`
> Step 1 is internally contradictory: the heading says IFormFile binding is "not automatic", but the snippet then states it's bound automatically in .NET 8.

### copilot-pull-request-reviewer — `plugins/aspnetcore/skills/minimal-api-file-upload/SKILL.md` L144
> The content-type allowlist includes "image/gif" but the subsequent magic-bytes validation only recognizes JPEG/PNG, so GIF uploads would pass the ContentType check and then fail later.

### copilot-pull-request-reviewer — `plugins/aspnetcore/skills/minimal-api-file-upload/SKILL.md`
> The example reads 8 bytes from the stream but doesn't verify how many bytes were actually read.

### copilot-pull-request-reviewer — `plugins/aspnetcore/skills/minimal-api-file-upload/SKILL.md`
> This states that IFormFile buffers the entire file in memory by default, which is misleading in ASP.NET Core.

### copilot-pull-request-reviewer — `plugins/aspnetcore/skills/minimal-api-file-upload/SKILL.md`
> The sample calls `context.Request.GetMultipartBoundary()`, but there is no such built-in API.

### copilot-pull-request-reviewer — `plugins/aspnetcore/skills/minimal-api-file-upload/SKILL.md` L198
> In the streaming example, the saved filename's extension is taken from the user-supplied filename. Prefer deriving the extension from validated content.

### copilot-pull-request-reviewer — `tests/aspnetcore/minimal-api-file-upload/eval.yaml` L3
> The PR description's file list references `plugins/dotnet/...` and `tests/dotnet/...`, but this PR adds the skill under `plugins/aspnetcore/...` and `tests/aspnetcore/...`.

### copilot-pull-request-reviewer — `plugins/aspnetcore/skills/minimal-api-file-upload/SKILL.md` L25
> The markdown table under **Inputs** uses `||` at the start of each row/header, which doesn't render as a proper table.

### copilot-pull-request-reviewer — `plugins/aspnetcore/skills/minimal-api-file-upload/SKILL.md` L138
> In the magic-bytes example, the stream is rewound (`stream.Position = 0;`) but the subsequent save uses `file.CopyToAsync(...)`, which opens a new stream and ignores the rewound one.

### mikekistler — `plugins/aspnetcore/skills/minimal-api-file-upload/SKILL.md` L147 (2026-03-28)
> I think another valid approach is to validate the filename provided before using it. Often the filename provided by the user has some significance and must be retained.

---

## Old PR #155 (closed) — Add minimal-api-file-upload skill

**State:** Closed (not merged) | **Author:** mrsharm | **Created:** 2026-03-02 | **Closed:** 2026-03-06

### Discussion Comments

#### mrsharm (2026-03-02) — Eval Results
> ### 3-Run Validation: +38.9% PASS
>
> | Metric | Value |
> |--------|-------|
> | Overall Improvement | **+38.9%** |
> | Confidence Interval | [+9.1%, +62.5%] significant |
> | Effect Size (g) | +100.0% |
> | Baseline Quality | 3.0/5 |
> | Skill Quality | 5.0/5 |
>
> Baseline consistently misses: Only configures one of the two required size limits, relies on ContentType alone for validation, uses user-provided filenames without sanitization.

#### ViktorHofer (2026-03-04)
> As discussed offline in the "dotnet/skills content" chat, this PR will need to be re-submitted from a connected fork. Also please update this PR based on the new repo folder structure (plugins instead of src).

#### mrsharm (2026-03-06)
> Closing: replaced by new PR from mrsharm/skills with plugins/ directory structure.

### Review Comments (PR #155)

#### copilot-pull-request-reviewer — `src/dotnet/skills/minimal-api-file-upload/SKILL.md` L43
> Step 1 is internally contradictory: it's titled as if IFormFile requires [FromForm] (not automatic), but later in this section it states IFormFile *is* bound automatically in .NET 8.

#### copilot-pull-request-reviewer — `src/dotnet/skills/minimal-api-file-upload/SKILL.md` L138
> The "safe filename" still uses `Path.GetExtension(file.FileName)`, which derives the extension from user-controlled input. Prefer choosing the extension based on the validated signature.

#### copilot-pull-request-reviewer — `src/dotnet/skills/minimal-api-file-upload/SKILL.md` L163
> This example uses `context.Request.GetMultipartBoundary()`, but that helper isn't defined anywhere in this repo and isn't a built-in ASP.NET Core API.

#### copilot-pull-request-reviewer — `src/dotnet/tests/minimal-api-file-upload/eval.yaml` L12
> Rubric criterion allows validating only `ContentType`, but the PR description explicitly calls out ContentType as client-spoofable. Tighten this rubric to require signature/magic-byte checking.

#### copilot-pull-request-reviewer — `src/dotnet/skills/minimal-api-file-upload/SKILL.md` L64
> These examples set the global Kestrel request body limit to 100MB, which can be confusing given the scenario is about enforcing a 10MB maximum.

#### copilot-pull-request-reviewer — `src/dotnet/skills/minimal-api-file-upload/SKILL.md` L125
> When reading magic bytes, the code ignores the return value from ReadAsync. Capture the bytes-read and fail fast if fewer than the required bytes are available.

#### copilot-pull-request-reviewer — `src/dotnet/skills/minimal-api-file-upload/SKILL.md` L118
> The allowed MIME types include `image/gif`, but the scenario/rubric for this skill is JPEG + PNG only.

#### copilot-pull-request-reviewer — `src/dotnet/skills/minimal-api-file-upload/SKILL.md` L153
> The statement that "IFormFile buffers the entire file in memory by default" is inaccurate for ASP.NET Core.

#### copilot-pull-request-reviewer — `src/dotnet/skills/minimal-api-file-upload/SKILL.md` L172
> `section.GetContentDispositionHeader()` / `contentDisposition.IsFileDisposition()` aren't built-in APIs.

#### timheuer — `src/dotnet/skills/implementing-form-file-uploads-minimal-apis/SKILL.md`
> This feels _too_ verbose a name -- "minimal-api-file-upload"?

#### timheuer — `src/dotnet/skills/implementing-form-file-uploads-minimal-apis/SKILL.md`
> Any validation that "8" is going to influence too much?

#### timheuer — `src/dotnet/skills/implementing-form-file-uploads-minimal-apis/SKILL.md`
> Strike "8" and put more information in the 'when to use'? (note below)

#### timheuer — `src/dotnet/skills/implementing-form-file-uploads-minimal-apis/SKILL.md`
> - File upload endpoints in ASP.NET minimal APIs (.NET 8+)

#### halter73 — `src/dotnet/skills/minimal-api-file-upload/SKILL.md` L108
> @GrabYourPitchforks @blowdart This should be okay for unauthenticated endpoints and endpoints using JWT bearer authentication, but I worry that this might cause people to disable antiforgery for endpoints authenticated with cookies. I wonder what the best way to communicate this potential security threat in the skill document.

---

# PR #200 — Add migrating-newtonsoft-to-system-text-json skill

**State:** Open | **Author:** mrsharm | **Created:** 2026-03-04 | **Comments:** 9
**Replaces:** #89

## Discussion Comments

### danmoseley (2026-03-07)
> now we have the new plugins factoring in main, this should not be in the general dotnet plugin. please move it to the dotnet-upgrade plugin which seems a better fit.

### timheuer (2026-03-09)
> This should go in `dotnet-upgrade` plugin

### ManishJayaswal (2026-03-10)
> @vijayrkn @tlmii @wtgodbe @noahfalk - please review

### abpiskunov (2026-03-10) — Detailed Skill Analysis
> I ran this new skill with Claude Opus 4.6 and pointed it to anthropics best practices link just in case and here what it said. Also i am wondering about evals file in this PR. I copied prompt from it to Gemini chat and Claude chat without this skill provided in fresh chat. Both generated same code that would pass evals 100% as far as i can tell... That raises question about the skill content and how efficient are evals.
>
> **What to Keep (High Value):**
> - Behavioral differences table (Step 1) — enforces completeness
> - ConfigureJsonOptions method (Step 2) — specific combo Claude won't reliably produce in full
> - Attribute mapping table (Step 3) — quick-reference, enforces complete coverage
> - Common Pitfalls table — highest-value section
> - Validation checklist — enforces completeness
> - grep commands for finding Newtonsoft usages
>
> **What to Cut or Compress:**
> - Full converter before/after code (Step 4) — Replace with "Key differences in converter API" bullet list only
> - Full JToken/JObject replacement examples (Step 5) — Keep the mapping table, cut the JsonNode mutable DOM code example
> - Polymorphic serialization section (Step 6) — Cut entirely. Claude knows [JsonDerivedType] thoroughly
> - Package reference / using statement changes (Step 7) — Cut the full XML and using-statement blocks
> - The "When Not to Use" section — "already using System.Text.Json" and "user wants to keep Newtonsoft" are obvious
>
> **What to Add:**
> - Conditional workflow at the top (ASP.NET Core app vs class library vs console/worker app)
> - Copy-and-track checklist — Restructure validation section into pattern with checkboxes
> - Explicit feedback loop — After each major step, add: "Run dotnet build to verify no compilation errors"
> - More trigger keywords in the description — "Json.NET", "removing Newtonsoft dependency", "serialization breaking changes", "AOT compatibility"
>
> **Net Effect:** ~250 lines → ~140-160 lines, focused on completeness-enforcement content. ~40% token reduction while increasing practical value.

### danmoseley (2026-03-28)
> evaluation looks good. @mrsharm you just need review signoffs — particularly @eiriktsarpalis who made the requested change review. I believe I addressed that.

## Review Comments (PR #200)

### copilot-pull-request-reviewer — `plugins/dotnet/skills/migrating-newtonsoft-to-system-text-json/SKILL.md` (resolved)
> This repeats the earlier claim that `JsonExtensionData` "must" use `Dictionary<string, JsonElement>` and that `Dictionary<string, object>` is invalid. System.Text.Json supports extension data on `IDictionary<string, object>` as well.

### copilot-pull-request-reviewer — `tests/dotnet-upgrade/.../eval.yaml` L5 (resolved)
> This eval is being added under `src/dotnet/tests/...`, but the repo layout expects scenarios under `tests/<plugin>/<skill>/eval.yaml`.

### copilot-pull-request-reviewer — `plugins/dotnet/skills/.../SKILL.md` (resolved)
> `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull` is not Newtonsoft.Json's default (Json.NET includes nulls unless configured).

### copilot-pull-request-reviewer — `tests/dotnet/.../eval.yaml` (resolved)
> The rubric's casing note is inconsistent with the skill doc.

### copilot-pull-request-reviewer — `tests/dotnet/.../eval.yaml` (resolved)
> `expect_tools: ["bash"]` forces the model to make at least one bash tool call. This prompt is primarily a code-migration explanation and doesn't require shell usage.

### copilot-pull-request-reviewer — `plugins/dotnet/skills/.../SKILL.md` (resolved)
> This SKILL.md is wrapped in a fenced code block (```skill ... ```), which means the YAML frontmatter and the rest of the content won't be parsed/rendered like other skills.

### copilot-pull-request-reviewer — `plugins/dotnet-upgrade/skills/.../SKILL.md` L223 (resolved)
> The polymorphism section implies System.Text.Json will automatically emit a discriminator for `[JsonDerivedType]` types. STJ polymorphism requires explicit opt-in/configuration.

### copilot-pull-request-reviewer — `plugins/dotnet/skills/.../SKILL.md` (resolved)
> The doc contradicts itself on default property naming.

### copilot-pull-request-reviewer — `plugins/dotnet/skills/.../SKILL.md` (resolved)
> `[JsonExtensionData]` doesn't have to be `Dictionary<string, JsonElement>` only.

### copilot-pull-request-reviewer — `plugins/dotnet-upgrade/skills/.../SKILL.md` L215 (resolved)
> This example configures `JsonSerializerSettings` with `TypeNameHandling = TypeNameHandling.Auto`, which is a known unsafe pattern when used with untrusted JSON.

### GrabYourPitchforks — `plugins/dotnet/skills/.../SKILL.md` (resolved, 2 comments)
> Setting `Preserve` significantly increases the attack surface of JSON deserialization. It shouldn't be promoted as a "try it and see if it solves your problem" mechanism.
>
> To defend against this attack, the application should consider what happens if the adversary controls the _edges_ in the object graph, not just the _values_ of the nodes.

### GrabYourPitchforks / eiriktsarpalis — `plugins/dotnet-upgrade/skills/.../SKILL.md` L88 (resolved, 3 comments)
> **GrabYourPitchforks:** Similarly, this is a potentially dangerous setting [ReadCommentHandling] and should not be encouraged unless the application has thought through the consequences. We chose not to allow this by default in S.T.J because it could lead to desynced deserialization attacks.
>
> **GrabYourPitchforks:** To defend against this attack, the application should: 1) ensure all deserializers operate in strict RFC compliance mode; or 2) ensure all components use the same deserializer library; or 3) ensure the frontend reserializes the payload before sending to backend.
>
> **eiriktsarpalis:** I would add that case insensitive property handling similarly makes systems susceptible to interoperability attacks. It should start off assuming the highest bar possible (using `JsonSerializerDefaults.Strict`) and then instruct the agent to interview the user about particular requirements that force the loosening of individual settings.

### JamesNK — `plugins/dotnet/skills/.../SKILL.md` (resolved)
> I think just always recommend JsonNode and friends. It's closer than JsonDocument/JsonElement.

### JamesNK / eiriktsarpalis — `plugins/dotnet-upgrade/skills/.../SKILL.md` L41 (resolved, 3 comments)
> **JamesNK:** STJ also escapes non-ASCII characters by default. Should recommend using relaxed encoder.
>
> **eiriktsarpalis:** I think we should caveat any such recommendation with the trade-offs: escaped JSON strings are semantically equivalent to unescaped ones.
>
> **JamesNK:** I'll leave it up to you. But if it's text being embedded in an HTML page then asp.net core will escape HTML characters anyway. I've never understood why STJ does this.

### eiriktsarpalis — `plugins/dotnet/skills/.../SKILL.md` (resolved)
> I'm pretty sure newer models can figure out regexes like that on their own, so I would just skip that part for fear of it going out of sync.

### ViktorHofer — `PR-FEEDBACK.md` (resolved)
> Remove this file

### eiriktsarpalis — `plugins/dotnet-upgrade/skills/.../SKILL.md` L14 (**open**, 2026-03-30)
> Consider adding an explicit instruction to the agent that it should be adding baseline serialization tests for the application models while performing the migration.

### eiriktsarpalis — `plugins/dotnet-upgrade/skills/.../SKILL.md` L284 (**open**, 2026-03-30)
> Consider adding links to the official STJ docs at learn.

### eiriktsarpalis — `tests/dotnet-upgrade/.../eval.yaml` L2 (**open**, 2026-03-30)
> Shouldn't we add evals covering more scenarios? Custom converters? Naming policies? etc? Consider mining real-world snippets either from dotnet org repos or public github repos in general (e.g. via grep.app)

---

## Old PR #89 (closed) — Add migrating-newtonsoft-to-system-text-json skill

**State:** Closed (not merged) | **Author:** mrsharm | **Created:** 2026-02-23 | **Closed:** 2026-03-04

### Discussion Comments

#### mrsharm (2026-02-25) — Eval Results (posted twice)
> | Skill | Test | Baseline | With Skill | Δ | Verdict |
> |-------|------|----------|------------|---|---------|
> | migrating-newtonsoft-to-system-text-json | Migrate model with Newtonsoft.Json attributes to System.Text.Json | 4.0/5 | 4.3/5 | +0.3 | ✅ |
>
> **Overall improvement: +10.8%** (3 runs, not statistically significant)

#### mrsharm (2026-02-26)
> Any feedback here? @ericstj @mcastro-x?

### Review Comments (PR #89)

#### copilot-pull-request-reviewer — `src/dotnet/skills/.../SKILL.md` (resolved)
> The claim that System.Text.Json "throws by default (.NET 8+)" for extra JSON properties is incorrect. System.Text.Json ignores extra JSON properties by default across all versions.

#### copilot-pull-request-reviewer — `src/dotnet/skills/.../SKILL.md` (resolved)
> The claim about null to non-nullable value type behavior is misleading. Both Newtonsoft.Json and System.Text.Json set non-nullable value types to their default values.

#### copilot-pull-request-reviewer — `src/dotnet/skills/.../SKILL.md` L68
> The comment "Newtonsoft default" on line 68 is misleading. PropertyNameCaseInsensitive is not a Newtonsoft.Json default behavior.

#### copilot-pull-request-reviewer — `src/dotnet/skills/.../SKILL.md` (resolved)
> The claim that Newtonsoft.Json uses "camelCase by default" for property naming is incorrect.

#### copilot-pull-request-reviewer — `src/dotnet/tests/.../eval.yaml` L45
> The rubric item claims PropertyNameCaseInsensitive should be configured "to match Newtonsoft default case-insensitive behavior", but Newtonsoft.Json is case-sensitive by default.

#### copilot-pull-request-reviewer — `src/dotnet/skills/.../SKILL.md` L5
> The skill metadata format is incorrect. Skills in this repository use standard YAML frontmatter delimited by `---`, not code fence blocks with ` ```skill`.

#### copilot-pull-request-reviewer — `src/dotnet/skills/.../SKILL.md` L44
> The claim that Newtonsoft.Json is "case-insensitive" by default is incorrect.

#### copilot-pull-request-reviewer — `src/dotnet/tests/.../eval.yaml`
> The prompt asks to "match Newtonsoft.Json's default behavior", but this will lead to incorrect configuration guidance.

#### copilot-pull-request-reviewer — `src/dotnet/tests/.../eval.yaml`
> The rubric expects warnings about "default casing" as a behavioral difference, but since both libraries use the same default casing, this warning would be misleading.

#### copilot-pull-request-reviewer — `src/dotnet/skills/.../SKILL.md` L243
> The pitfall "Forgetting PropertyNameCaseInsensitive = true" implies this is a required configuration to match Newtonsoft.Json behavior, but this is not accurate.

#### copilot-pull-request-reviewer — `src/dotnet/skills/.../SKILL.md` L67
> The comment "Newtonsoft default" on line 67 is incorrect. Newtonsoft.Json does not use camelCase by default.

---

# PR #268 — Add configuring-opentelemetry-dotnet skill

**State:** Open | **Author:** mrsharm | **Created:** 2026-03-06 | **Comments:** 12
**Replaces:** #91

## Discussion Comments

### mrsharm (2026-03-06)
> **Migration Note**
>
> This PR replaces #91 which was opened from `mrsharm/skills-old`. All prior review feedback from #91 still applies.

### mrsharm (2026-03-06) — Feedback carried over from #91
> | Reviewer | Feedback |
> |----------|----------|
> | copilot | Missing `using OpenTelemetry.Trace;` for `SetStatus()`/`RecordException()` |
> | copilot | File wrapped in `` ```skill `` code block — metadata won't parse |
> | copilot | `return order;` references undefined variable |
> | **tarekgh** | Does `SetDbStatementForText` exist in latest SqlClient instrumentation? |
> | **tarekgh** | Does it need to reference `OpenTelemetry.Instrumentation.Runtime` package? |
> | **tarekgh** | Traces don't configure endpoint but metrics do? |
> | **tarekgh** | Missing `using` directives |
> | **tarekgh** | Is `GetQueueDepth` just demonstrating the idea? |
> | **tarekgh** | Is HttpClient instrumentation accurate for clients not from `IHttpClientFactory`? |
> | **noahfalk** | Fix package list (suggestion provided) |
> | **noahfalk** | Fix description (suggestion provided) |
> | **noahfalk** | SQL instrumentation should be clearly marked **optional** |
> | **noahfalk** | Runtime metrics should be marked **optional** |
> | **noahfalk** | Custom tracing spans should also be optional (but more useful) |
> | **noahfalk** | Use `IMeterFactory` instead of static `Meter` per official guidance |
> | **noahfalk** | What about logs/metrics verification? |
> | **noahfalk** | `IMeterFactory` again for custom metrics section |
> | **noahfalk** | Eval prompts should be simpler/more generalized |

### danmoseley (2026-03-07)
> Now we have new plugin factoring, moved the skill from `plugins/dotnet/` to `plugins/dotnet-diag/`. OpenTelemetry is an observability/diagnostics concern and fits better alongside the other diagnostic skills.

### ManishJayaswal (2026-03-10)
> @BrennanConroy @adityamandaleeka - please review. @mrsharm - you may want to update the branch. This skill should also be under ASP plugin.

### danmoseley (2026-03-24)
> @mrsharm this seems ASP.NET specific? If so should probably go in the dotnet-aspnet plugin not dotnet-diag?

### danmoseley (2026-03-28)
> @mrsharm to get this restarted again for you I got to copilot review clean, and also got copilot to drive up the score using the instructions below the eval table. That looks pretty good too. I did _not_ review the changes it made. Hope its OK for you to do that...

## Review Comments (PR #268)

### copilot-pull-request-reviewer — `tests/dotnet-diag/.../eval.yaml` (resolved)
> Scenario rubric lists "correct packages" but omits `OpenTelemetry.Instrumentation.Http`.

### copilot-pull-request-reviewer — `plugins/dotnet-diag/skills/.../SKILL.md` (resolved)
> `SKILL.md` frontmatter is wrapped in a fenced code block (```skill). Remove the code fence.

### copilot-pull-request-reviewer — `plugins/dotnet-diag/skills/.../SKILL.md` (resolved)
> Step 1 says to "install exactly these" packages, but Step 2 uses `AddSqlClientInstrumentation()` and `AddRuntimeInstrumentation()` unconditionally.

### copilot-pull-request-reviewer — `plugins/dotnet-aspnet/skills/.../SKILL.md` L183 (resolved)
> The `ProcessOrderAsync` example returns `order`, but no `order` variable is defined in the snippet.

### copilot-pull-request-reviewer — `plugins/dotnet-aspnet/skills/.../SKILL.md` L237 (resolved)
> `TagList` is used but not in scope with the shown `using System.Diagnostics.Metrics;`.

### copilot-pull-request-reviewer — `plugins/dotnet-diag/skills/.../SKILL.md` (resolved)
> The context propagation snippet uses `Propagators`, `PropagationContext`, and `Baggage` without showing the required namespaces.

### copilot-pull-request-reviewer — Multiple files (resolved)
> Step 1 says "Install exactly these", but later code samples use additional APIs requiring more NuGet packages. Also the PR description lists wrong plugin paths. Various CODEOWNERS issues.

### copilot-pull-request-reviewer — `plugins/dotnet-aspnet/skills/.../SKILL.md` (resolved)
> Tracing config explicitly sets the OTLP endpoint, but metrics and logging call exporter with defaults — can lead to different endpoints.

### copilot-pull-request-reviewer — `plugins/dotnet-aspnet/skills/.../SKILL.md` L190 (resolved)
> The OrderService sample returns `order`, but no `order` variable is created/assigned.

### copilot-pull-request-reviewer — `plugins/dotnet-aspnet/plugin.json` L6 (resolved)
> This PR introduces a new plugin (plugins/dotnet-aspnet), but it is not registered in the plugin marketplaces.

### copilot-pull-request-reviewer — `plugins/dotnet-aspnet/skills/.../SKILL.md` L188 (resolved)
> The `activity?.RecordException(ex)` call relies on the OpenTelemetry Trace extension method. As written, snippet only imports `System.Diagnostics`.

### copilot-pull-request-reviewer — `plugins/dotnet-aspnet/skills/.../SKILL.md` L135 (resolved)
> Tracing/metrics configure explicit OTLP endpoint, but logging uses `logging.AddOtlpExporter()` without setting endpoint.

### copilot-pull-request-reviewer — `plugins/dotnet-aspnet/skills/.../SKILL.md` L215 (resolved)
> The custom metrics example uses `IMeterFactory`, but the snippet doesn't include the namespace/import for it.

### copilot-pull-request-reviewer — `plugins/dotnet-aspnet/skills/.../SKILL.md` L12 (resolved)
> The skill says it's used for setting up exporters "(OTLP, Jaeger, Prometheus)", but the workflow only shows OTLP/Console configuration.

### copilot-pull-request-reviewer — `plugins/dotnet-aspnet/skills/.../SKILL.md` L27 (resolved)
> The Inputs table uses a leading double `||` on each row.

### copilot-pull-request-reviewer — `plugins/dotnet-aspnet/skills/.../SKILL.md` L299 (resolved)
> The Common Pitfalls table also uses a leading double `||`.

### copilot-pull-request-reviewer — `plugins/dotnet-aspnet/skills/.../SKILL.md` L39/L43 (resolved)
> Package list in Step 1 doesn't include the package that provides `builder.Logging.AddOpenTelemetry(...)` or `OtlpExportProtocol`.

### copilot-pull-request-reviewer — `plugins/dotnet-aspnet/skills/.../SKILL.md` L27 (resolved)
> Inputs table suggests "Jaeger, Prometheus (all accept OTLP)". Prometheus does not accept OTLP directly.

### copilot-pull-request-reviewer — `plugins/dotnet-aspnet/skills/.../SKILL.md` (resolved)
> Pitfall "Missing HTTP client spans" states `AddHttpClientInstrumentation()` only works with `HttpClient` from DI. This is inaccurate — it works for `new HttpClient()` too.

### copilot-pull-request-reviewer — `tests/dotnet-aspnet/.../eval.yaml` L37 (resolved)
> The second scenario is a negative activation test but doesn't set `expect_activation: false`.

---

## Old PR #91 (closed) — Add configuring-opentelemetry-dotnet skill

**State:** Closed (not merged) | **Author:** mrsharm | **Created:** 2026-02-23 | **Closed:** 2026-03-06

### Discussion Comments

#### tarekgh (2026-02-24)
> CC @JamesNK @rajkumar-rangaraj

#### mrsharm (2026-02-25) — Eval Results
> | Skill | Test | Baseline | With Skill | Δ | Verdict |
> |-------|------|----------|------------|---|---------|
> | configuring-opentelemetry-dotnet | Add OpenTelemetry tracing and metrics to an ASP.NET Core API | 5.0/5 | 4.3/5 | -0.7 | ✅ |
> | configuring-opentelemetry-dotnet | OpenTelemetry skill should not activate for simple logging question | 5.0/5 | 5.0/5 | 0.0 | ✅ |
>
> **Overall improvement: +15.3%** (3 runs, not statistically significant)

#### noahfalk (2026-02-25)
> Just curious, have you compared any results between writing all this guidance inline vs. having the skill reference pre-existing docs? The skill is certainly more compact, but its not clear to me how the tradeoff between compactness vs. depth/breadth affects the results. (I'm also not sure we have enough test cases to draw much conclusion from the automated results alone)

#### steveisok (2026-02-25)
> External docs inform; inline skills steer. Pointing to docs puts more faith in the LLM figuring it out. Doing it inline allows you a much more direct way to influence.

#### rajkumar-rangaraj (2026-02-25)
> I recently updated the **OpenTelemetry .NET documentation** to improve how OpenTelemetry configuration is explained and structured. We intentionally **separated the builder-based configuration docs** so they can be consumed by **agents and automation workflows**. It might be worth borrowing a similar approach for skills documentation by:
> - Clearly separating **what needs to be configured** from **how it is wired together**
> - Identifying which parts are **agent-friendly** vs purely human-oriented examples

#### rajkumar-rangaraj (2026-02-25)
> Adding OpenTelemetry .NET maintainers to get their perspective too. @alanwest @Kielek @martincostello

#### mrsharm (2026-03-06)
> Closing: replaced by new PR from mrsharm/skills with plugins/ directory structure.

### Review Comments (PR #91)

#### copilot-pull-request-reviewer — `src/dotnet/skills/.../SKILL.md` L166
> In the spans example, `SetStatus(...)` and `RecordException(...)` are OpenTelemetry extension methods on `Activity` (namespace `OpenTelemetry.Trace`). The snippet only includes `using System.Diagnostics;`, so it won't compile.

#### copilot-pull-request-reviewer — `src/dotnet/skills/.../SKILL.md` L6
> SKILL.md is wrapped in a fenced ```skill code block, but the skill validator only parses YAML frontmatter when the file starts with `---`.

#### copilot-pull-request-reviewer — `src/dotnet/skills/.../SKILL.md` L161
> In the OrderService example, `return order;` references an `order` variable that isn't defined in the snippet.

#### tarekgh — `src/dotnet/skills/.../SKILL.md` L85
> does `SetDbStatementForText` exist in the latest SqlClient instrumentation?

#### tarekgh — `src/dotnet/skills/.../SKILL.md` L101
> does it need to reference OpenTelemetry.Instrumentation.Runtime package?

#### tarekgh — `src/dotnet/skills/.../SKILL.md` L104
> these doesn't need to configure the endpoint as you did with metrics?

#### tarekgh — `src/dotnet/skills/.../SKILL.md` L225
> do we need `using` directives here?

#### tarekgh — `src/dotnet/skills/.../SKILL.md` L203
> I assume `GetQueueDepth` just demonstrating the idea and doesn't have to exist in the code.

#### tarekgh — `src/dotnet/skills/.../SKILL.md` L260
> Is this accurate? wouldn't the instrumentation still work with the spans created from clients not created from IHttpClientFactory?

#### noahfalk — `src/dotnet/skills/.../SKILL.md` L27
> (suggestion) Observability backend | No | Where to export: Jaeger, Prometheus, OTLP collector, Aspire Dashboard

#### noahfalk — `src/dotnet/skills/.../SKILL.md` L17
> (suggestion) The user's application doesn't use ASP.NET

#### noahfalk — `src/dotnet/skills/.../SKILL.md` L88
> Many apps probably have no need to do this [SQL instrumentation]. You might want to segregate this into another step that is clearly optional.

#### noahfalk — `src/dotnet/skills/.../SKILL.md` L121
> I'd recommend marking this [runtime metrics] optional. I suspect many customers don't need this.

#### noahfalk — `src/dotnet/skills/.../SKILL.md` L175
> This [custom tracing spans] should also be optional, though probably more apps would benefit from it.

#### noahfalk — `src/dotnet/skills/.../SKILL.md` L183
> The official guidance is for apps using DI to use IMeterFactory rather than creating a static Meter.

#### noahfalk — `src/dotnet/skills/.../SKILL.md` L246
> And logs/metrics?

#### noahfalk — `src/dotnet/skills/.../SKILL.md` L263
> IMeterFactory again :)

#### noahfalk — `src/dotnet/tests/.../eval.yaml` L4
> I'd recommend either adding more eval prompts, or if we will only have a limited number focus on prompts that are simpler and more generalized. I'd guess prompts like these are going to be more common: "Please enable telemetry for my app", "Help set up OpenTelemetry", "I want to record some metrics, how do I do that?"

---

# PR #269 — Add refactoring-to-async skill

**State:** Open | **Author:** mrsharm | **Created:** 2026-03-06 | **Comments:** 36
**Replaces:** #79

## Discussion Comments

### mrsharm (2026-03-06)
> **Migration Note**
>
> This PR replaces #79. All prior review feedback from #79 still applies.

### mrsharm (2026-03-06) — Feedback carried over from #79
> **@Copilot** — `eval.yaml` L12: The scenario requires identifying sync-over-async patterns like `.Result`/`.Wait()`, but the `output_not_matches` assertion fails the run if the agent output contains `.Result` or `.Wait()` anywhere (including when calling them out as anti-patterns).
>
> **@Copilot** — `SKILL.md` L35: The `grep` pattern uses `\b` for a word-boundary, but `grep`'s default regex syntax does not treat `\b` as a word boundary.
>
> **@Copilot** — `SKILL.md` L155: Same issue as Step 1: `grep` without `-P` won't treat `\b` as a word boundary.
>
> **@danmoseley** — `SKILL.md` L8: move any part of use/not use into description similar to runtime formatting, if it can avoid unnecessary reloading.
>
> **@danmoseley** — `SKILL.md` L167: this [ConfigureAwait(false)] is missing from all the examples. maybe indicate those are app code? It should be used consistently or not at all.
>
> **@danmoseley** — `SKILL.md` L185: this may be a bit high level/conceptual for this skill?
>
> **@danmoseley** — `SKILL.md` L53: (suggestion) add `(DbDataReader)` and `(DbCommand)` qualifiers to the sync→async mapping table.
>
> **@danmoseley** — `eval.yaml` L25: is this really something the user would write? seems like a gimme as written. more likely the user would say "make DoIt() async so it's faster" and the AI would have to figure out that it's CPU bound?
>
> **@danmoseley** — `eval.yaml` L33: do you expect `ConfigureAwait(false)` in this UserService case? either way, should be a test for the opposite case (eg winforms) and verify in each case it's present or not as expected.
>
> **@danmoseley** — `SKILL.md` L148: (suggestion to expand the error→fix table with more compiler error codes)

### ViktorHofer (2026-03-06)
> @mrsharm check the one scenario in which the skill doesn't get activated.

### mrsharm (2026-03-06)
> @ViktorHofer: Intentional - is a negative test and is the correct outcome. The eval suggests optimizing matrix multiplication (CPU-bound), not about converting I/O to async. The skill's description targets I/O-bound async refactoring, so the agent should recognize this isn't an async problem and not load the skill.

### ViktorHofer (2026-03-08)
> @mrsharm such tests must be annotated with `expect_activation: false`. Otherwise we have no way to distinguish between intentionally not activated and unintentionally not activated.

### mrsharm (2026-03-08)
> @ViktorHofer - thanks! Been implemented for this and the other PRs.

### ManishJayaswal (2026-03-10)
> @mrsharm - the repo has undergone some restructuring. This skill is already under the correct plugin - dotnet. Please update the PR and submit again.
> @jasonmalinowski @jaredpar - please review.

### mrsharm (2026-03-06)
> @danmoseley - could you please take another look? I believe I have addressed your feedback.

## Review Comments (PR #269)

### copilot-pull-request-reviewer — `tests/dotnet/refactoring-to-async/UserService.cs` (resolved)
> The comment says "Sync-over-async: blocking call", but the code is using the synchronous `HttpClient.Send(...)` API. Either adjust the comment or change the sample to a true sync-over-async pattern.

### copilot-pull-request-reviewer — `tests/dotnet/refactoring-to-async/eval.yaml` L3 (resolved)
> PR description 'Files' list appears incomplete relative to the actual changes.

### copilot-pull-request-reviewer — `tests/dotnet/refactoring-to-async/eval.yaml` (resolved)
> `output_not_matches` is currently broad enough that it will also fail when the model *mentions* `.Result` / `.Wait()` as anti-patterns in prose. Consider tightening the regex.

### copilot-pull-request-reviewer — `tests/dotnet/refactoring-to-async/eval.yaml` (resolved)
> The CPU-bound scenario's `output_not_matches` pattern includes `async Task`, which can easily appear in an explanation.

### copilot-pull-request-reviewer — `plugins/dotnet/skills/refactoring-to-async/SKILL.md` (resolved)
> The Stream `ReadAsync`/`WriteAsync` examples don't match the actual Stream APIs. Recommend updating to use the correct overloads.

### copilot-pull-request-reviewer — `tests/dotnet/refactoring-to-async/UserService.cs` (resolved)
> `_httpClient` is injected and stored but never used in `UserRepository`.

### mrsharm — `.github/CODEOWNERS` (resolved)
> Update this to the right owner. @danmoseley / @timheuer - who should be the right owner for this skill?

### copilot-pull-request-reviewer — `tests/dotnet/refactoring-to-async/eval.yaml` (resolved)
> `output_not_matches` regex will fail if the response mentions `.GetAwaiter().GetResult()` in explanatory text.

### copilot-pull-request-reviewer — `tests/dotnet/refactoring-to-async/eval.yaml` (resolved)
> The `ApiClient.cs` fixture uses `StreamReader` but doesn't include `using System.IO;`.

### copilot-pull-request-reviewer — `.github/CODEOWNERS` (resolved, 2 comments)
> These CODEOWNERS entries for the new dotnet skill/tests are placed under the `# dotnet-upgrade` section. Move them under the `# dotnet` section.

### copilot-pull-request-reviewer — `plugins/dotnet/skills/refactoring-to-async/SKILL.md` (resolved)
> PR description text appears to contain a duplicated sentence at the end.

### jaredpar — `plugins/dotnet/skills/refactoring-to-async/SKILL.md` (resolved, 2 comments)
> **jaredpar:** The budget for skills descriptions is limited. This seems longer than I would expect for a skill.
>
> **danmoseley:** We only validate the hard limit of 1024 chars. Beyond that, it's hard to score whether there are too many (waste) or appropriate (prevents loading the skill inappropriately).

### jaredpar / danmoseley — `tests/dotnet/refactoring-to-async/eval.yaml` (resolved, 2 comments)
> **jaredpar:** I worry that the prompt here is too strongly connected to the prompts in the skill. Basically the skill says "for ASP.NET do X" and the eval says "this is ASP.NET". It feels like the test is being fitted for the skill vs. the user experience. My intuition is that customers would more naturally write: "This controller has performance issues under load..."
>
> **danmoseley:** yes, I agree this is arguably "overfitting". Current overfitting judge doesn't catch it. @JanKrivanek I wonder whether this suggests we should tune prompt given to the overfitting judge.

### danmoseley — `tests/dotnet/refactoring-to-async/UserService.cs` L23 (resolved)
> should the skill recommend using `IAsyncDisposable` (ie `await using`)? and eval should check that all these `using var` get converted.

### danmoseley — `plugins/dotnet/skills/refactoring-to-async/SKILL.md` (resolved)
> (suggestion) Missing `ConfigureAwait(false)` in libraries | Can deadlock in Winforms/WPF/ASP.NET sync contexts | Add `.ConfigureAwait(false)` to every `await` in library code; omit in ASP.NET Core app code (no SynchronizationContext)

### danmoseley — `tests/dotnet/refactoring-to-async/eval.yaml` (resolved)
> AI says, "Task.WhenAll fires all HTTP requests concurrently, which could overwhelm a downstream service. Sequential await should be the preferred default; WhenAll should only be suggested with a concurrency caveat."

### danmoseley — `plugins/dotnet/skills/refactoring-to-async/SKILL.md` L212 (resolved)
> There should be a bit more mention of `ValueTask` in the guidance higher up and its limitations over `Task`. Eg., cannot be awaited multiple times, cannot be stored.

### danmoseley — `plugins/dotnet/skills/refactoring-to-async/SKILL.md` L185 (resolved)
> Is `IAsyncEnumerable` / `await foreach` in scope? Probably should be. If not, it should say it's out of scope. Similar for `IAsyncDisposable`.

### danmoseley — `plugins/dotnet/skills/refactoring-to-async/SKILL.md` (resolved)
> Suggest to provide a list of example method names, rather than write the grep for it. It's great at writing grep, and giving it the command suggests that this is a complete list. Plus if this hits something inappropriate eg a random property named "Result" it can adjust itself.

### copilot-pull-request-reviewer — `tests/dotnet/refactoring-to-async/eval.yaml` L177 (resolved)
> The controller scenario rubric requires removing sync-over-async anti-patterns, but the assertions only check for an async signature + CancellationToken. Add an `output_not_matches` assertion.

### copilot-pull-request-reviewer — `plugins/dotnet/skills/refactoring-to-async/SKILL.md` (resolved)
> This CancellationToken example also leaves the `HttpResponseMessage` undisposed. Should use `using var response = await ...`.

### copilot-pull-request-reviewer — `plugins/dotnet/skills/refactoring-to-async/SKILL.md` (resolved)
> In the async "After" example, `HttpResponseMessage` is not disposed. Should demonstrate disposing it.

### copilot-pull-request-reviewer — `.github/CODEOWNERS` (resolved)
> CODEOWNERS entries placed under the `# dotnet-upgrade` section — should be under `# dotnet`.

### copilot-pull-request-reviewer — `tests/dotnet/refactoring-to-async/eval.yaml` (resolved)
> Scenario 1's rubric calls out removing `.GetAwaiter().GetResult()` sync-over-async, but the `output_not_matches` assertion only checks for `.Result` and `.Wait()`.

---

## Old PR #79 (closed) — Add refactoring-to-async skill

**State:** Closed (not merged) | **Author:** mrsharm | **Created:** 2026-02-23 | **Closed:** 2026-03-06

### Discussion Comments

#### ViktorHofer (2026-02-23)
> Please share the results as a comment similar to #75. Also make sure that you test at least 3 runs (`--runs 3` for local validation).

#### mrsharm (2026-02-25) — Eval Results (posted twice)
> | Skill | Test | Baseline | With Skill | Δ | Verdict |
> |-------|------|----------|------------|---|---------|
> | refactoring-to-async | Refactor synchronous service to async | 3.3/5 | 5.0/5 | +1.7 | ✅ |
> | refactoring-to-async | Async refactoring should not apply to CPU-bound code | 1.0/5 | 1.0/5 | 0.0 | ✅ |
>
> **Overall improvement: +18.8%** (3 runs, not statistically significant)

#### mrsharm (2026-02-25)
> > Please share the results as a comment similar to #75. Also make sure that you test at least 3 runs.
>
> Done.

#### danmoseley (2026-03-02)
> add codeowners entry, move files around to match new pattern in main.

#### mrsharm (2026-03-06)
> Closing: replaced by new PR from mrsharm/skills with plugins/ directory structure.

### Review Comments (PR #79)

#### copilot-pull-request-reviewer — `src/dotnet/tests/refactoring-to-async/eval.yaml` L12
> The scenario requires identifying sync-over-async patterns like `.Result`/`.Wait()`, but the `output_not_matches` assertion fails the run if the agent output contains `.Result` or `.Wait()` anywhere (including when calling them out as anti-patterns).

#### copilot-pull-request-reviewer — `src/dotnet/skills/refactoring-to-async/SKILL.md` L35
> The `grep` pattern uses `\b` for a word-boundary, but `grep`'s default regex syntax does not treat `\b` as a word boundary (it's typically interpreted as a backspace).

#### copilot-pull-request-reviewer — `src/dotnet/skills/refactoring-to-async/SKILL.md` L155
> Same issue as Step 1: `grep` without `-P` won't treat `\b` as a word boundary.

#### danmoseley — `src/dotnet/skills/refactoring-to-async/SKILL.md` L8
> move any part of use/not use into description similar to runtime formatting, if it can avoid unnecessary reloading. Possibly not all can go there. For example: user wants to parallelize work is something that can be known before loading the skill, and avoid load.

#### danmoseley — `src/dotnet/skills/refactoring-to-async/SKILL.md` L167
> this [ConfigureAwait(false)] is missing from all the examples. maybe indicate those are app code? It might be worth mentioning as instructions, not just in anti-patterns — if code is library add `.ConfigureAwait(false)` to every await. It should be used consistently or not at all.

#### danmoseley — `src/dotnet/skills/refactoring-to-async/SKILL.md` L185
> this may be a bit high level/conceptual for this skill?

#### danmoseley — `src/dotnet/skills/refactoring-to-async/SKILL.md` L53
> (suggestion) Add `(DbDataReader)` and `(DbCommand)` qualifiers to the sync→async mapping table entries.

#### danmoseley — `src/dotnet/tests/refactoring-to-async/eval.yaml` L25
> is this really something the user would write? seems like a gimme as written. more likely the user would say "make DoIt() async so it's faster" and the AI would have to figure out that it's CPU bound?

#### danmoseley — `src/dotnet/tests/refactoring-to-async/eval.yaml` L33
> do you expect `ConfigureAwait(false)` in this UserService case? either way, should be a test for the opposite case (eg winforms) and verify in each case it's present or not as expected.

#### danmoseley — `src/dotnet/skills/refactoring-to-async/SKILL.md` L148
> (suggestion to expand the error→fix table with more compiler error codes like CS4032, CS0029, CS0127, CS1983, CS1998, CS0535, CS7036, CS1503)
