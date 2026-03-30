## Summary
Adds the **minimal-api-file-upload** skill for handling file uploads in ASP.NET Core 8+ minimal APIs.

> **Note:** Replaces #155 (migrated from skills-old repo to new plugins/ structure).

## What the Skill Teaches

The base model consistently gets several file upload patterns wrong. This skill addresses five key gaps:

1. **Dual size limits** — Must configure BOTH `Kestrel.MaxRequestBodySize` AND `FormOptions.MultipartBodyLengthLimit`. The base model typically configures only one, causing cryptic upload failures.
2. **Antiforgery auto-validation** — In .NET 8+, `UseAntiforgery()` silently rejects all form-bound uploads with 400 Bad Request. Must call `.DisableAntiforgery()` on API endpoints (with appropriate security caveats for cookie-auth).
3. **Magic byte validation** — The base model relies solely on `ContentType` which is client-spoofable. The skill teaches JPEG/PNG file signature verification.
4. **Safe filename generation** — The base model often uses `file.FileName` directly, enabling path traversal. The skill teaches GUID-based names and also covers sanitizing user-provided filenames when they must be retained (per reviewer feedback from @mikekistler).
5. **Streaming with MultipartReader** — For very large files (>500MB), IFormFile buffering causes OOM. The skill teaches direct-to-disk streaming via `MultipartReader`.

## Eval Results (3-run local validation)

| Scenario | Baseline | With Skill | Δ | Overfit | Verdict |
|----------|----------|------------|---|---------|---------|
| Implement secure file upload | 3.7/5 | **5.0/5** | +35% | 0.08 ✅ | ✅ |
| Upload multiple files with metadata | 3.3/5 | **5.0/5** | +52% | 0.08 ✅ | ✅ |
| Stream very large file uploads | 4.7/5 | **5.0/5** | +6% | 0.08 ✅ | ✅ |

Model: claude-opus-4.6 | Judge: claude-opus-4.6

## Files

- `plugins/dotnet-aspnet/plugin.json` (new plugin)
- `plugins/dotnet-aspnet/skills/minimal-api-file-upload/SKILL.md`
- `tests/dotnet-aspnet/minimal-api-file-upload/eval.yaml`

## Review feedback addressed

- Added filename sanitization as valid alternative to GUID-only approach (per @mikekistler)
- Removed code fence wrapper from SKILL.md frontmatter (per Copilot review)
- Removed `image/gif` from allowlist to match JPEG/PNG-only eval scope
- Fixed `GetMultipartBoundary()` — replaced with standard `HeaderUtilities` boundary parsing
- Fixed misleading "IFormFile buffers entirely in memory" claim — clarified memory threshold + temp file behavior
- Derived file extension from validated magic bytes instead of user-controlled `Path.GetExtension(file.FileName)`
- Added security caveat for `DisableAntiforgery()` on cookie-authenticated endpoints (per @halter73)
- Moved skill from `plugins/dotnet` to `plugins/dotnet-aspnet` per reviewer guidance
- Iterated eval scenarios through 5+ runs — removed noisy tests (baseline-perfect antiforgery diagnosis, non-activation JSON test with efficiency noise), simplified multi-file prompt to prevent agent project scaffolding overhead
