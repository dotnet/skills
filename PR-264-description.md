## Summary
Adds the **minimal-api-file-upload** skill for handling file uploads in ASP.NET Core 8 minimal APIs.

> **Note:** Replaces #134 (migrated from skills-old repo). Skill moved to `aspnetcore` plugin per repo restructuring.

## What the Skill Teaches
- `IFormFile` binding in minimal APIs (when `[FromForm]` is needed vs automatic)
- `IFormFileCollection` for multiple file uploads
- Security: file size limits, content type allowlists, magic byte validation
- Safe file naming with `Guid` + validated extension (never trust client filename)
- Streaming uploads with `MultipartReader` for large files
- Antiforgery considerations
- Path traversal prevention

## Eval Scenarios
- (eval.yaml needs scenarios — currently empty)

## Files
- `plugins/aspnetcore/plugin.json` — ASP.NET Core plugin
- `plugins/aspnetcore/skills/minimal-api-file-upload/SKILL.md` — skill instructions
- `tests/aspnetcore/minimal-api-file-upload/eval.yaml` — eval scenarios (to be added)
