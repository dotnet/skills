# Implementation Plan: Skill Package Upload and Validation

**Branch**: `002-skill-submission-ui` | **Date**: 2026-07-26 | **Spec**: [spec.md](./spec.md)

## Summary

Replace guided authoring with a read-only upload workflow. Contributors upload a repository-shaped ZIP or `SKILL.md`; the stateless API safely expands, parses, validates, previews, and normalizes it. Contributors fix source files externally and re-upload. No authoring controls, content execution, persistence, GitHub writes, or LLM calls are included.

## Technical Context

**Language/Version**: C# 14 / .NET 10; TypeScript 6 / React 19
**Dependencies**: ASP.NET Core multipart handling, System.IO.Compression, YamlDotNet, React Router, Fluent UI, React Markdown/GFM
**Storage**: Current upload held in browser memory; stateless request processing; no database or server persistence
**Testing**: xUnit unit/contract/security/performance tests, Vitest, Playwright, axe, production builds
**Performance**: Complete bounded-package validation within 3 seconds
**Constraints**: No extraction to disk, execution, editing, GitHub writes, or active HTML rendering; WCAG 2.2 AA

## Constitution Check

| Constraint | Status | Evidence |
|---|---|---|
| Deterministic validation first | PASS | Upload is statically parsed and validated; no model execution. |
| Secure repository content | PASS | Streaming limits, normalized paths, fail-closed ZIP handling, no execution. |
| Repository authoritative | PASS | Identity, domains, schema, and policy derive from checkout. |
| Accessible incremental value | PASS | Upload, inspect, and normalized download are independent journeys. |
| Existing stack/layout | PASS | Extends the existing .NET API and React/Fluent UI. |

## Project Structure

```text
src/SkillCatalog/
├── api/SkillCatalog.Api/
│   ├── Models/UploadModels.cs
│   ├── Services/SkillPackageParser.cs
│   ├── Services/UploadedSkillValidator.cs
│   ├── Services/NormalizedPackageService.cs
│   └── Endpoints/SubmissionEndpoints.cs
└── web/src/features/skill-submission/
    ├── SkillSubmissionPage.tsx
    └── components/{PackageDropzone,ValidationSummary,SubmissionPreview,PackageStep}.tsx
```

## Design

The browser sends multipart form data containing one bounded ZIP or Markdown file. The API reads entries without extracting to disk, rejects unsafe archive metadata before parsing, locates exactly one skill, parses supported metadata/evaluations, validates repository policy, and returns a content-safe preview plus an opaque upload revision. Because the API persists nothing, normalized download resends the same file and revalidates it; the browser enables download only for the current file fingerprint.

Post-design constitution check: PASS. Removing authoring reduces state, attack surface, and ambiguity while preserving repository compatibility.
