# Implementation Plan: Skill Catalog UI

**Branch**: `001-skill-catalog-ui` | **Date**: 2026-07-26 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-skill-catalog-ui/spec.md`

## Summary

Build a public, read-only catalog that discovers skills from the repository, exposes searchable metadata and safe per-skill downloads through an ASP.NET Core API, and presents a responsive React application using Fluent UI. Repository files remain authoritative; the API builds an immutable in-memory catalog snapshot and never executes skill content.

## Technical Context

**Language/Version**: C# 14 on .NET 10 LTS; TypeScript 5.x with React 19.2

**Primary Dependencies**: ASP.NET Core Minimal APIs, Microsoft.AspNetCore.OpenApi, YamlDotNet; React, Vite, Fluent UI React v9, React Router, DOMPurify

**Storage**: Repository filesystem is authoritative; immutable in-memory catalog snapshot; no database in v1

**Testing**: Microsoft.Testing.Platform with xUnit for API unit/integration/contract tests; Vitest, React Testing Library, Playwright, and axe-core for UI and accessibility tests

**Target Platform**: Linux container or cross-platform local process serving modern evergreen browsers; responsive layouts from 320px upward

**Project Type**: Web application with separate API and single-page front end

**Performance Goals**: Search/filter response p95 under 250ms for 500 skills; detail API p95 under 300ms; usable detail view within two seconds on normal broadband

**Constraints**: Anonymous and read-only; WCAG 2.2 AA; no execution of repository content; download path confinement; preserve upstream layout; no database or account system; OpenAPI contract is authoritative

**Scale/Scope**: Up to 500 skills and 10,000 indexed resources per snapshot; catalog, detail, resource preview, and individual ZIP download flows; evaluation UI is out of scope

## Constitution Check

- **Skills are versioned products**: PASS. Existing canonical skill structures and provenance are preserved.
- **Deterministic validation first**: PASS. Parsing, paths, packaging, contracts, accessibility, and security have deterministic tests; no LLM judge is used.
- **Independent roles**: PASS. This feature does not author, execute, or judge skills.
- **Reproducible evidence**: PASS. Responses identify catalog revision and refresh time.
- **Secure repository content**: PASS. Content is untrusted, never executed, sanitized for display, and download-confined.
- **Open agent compatibility**: PASS. Canonical files are exposed without runtime-specific rewrites.
- **Accessible incremental value**: PASS. User stories are independently testable and include WCAG 2.2 AA.

**Post-design re-check**: PASS. The data model, OpenAPI contract, security boundaries, and quickstart preserve every gate. No exceptions are required.

## Project Structure

### Documentation (this feature)

```text
specs/001-skill-catalog-ui/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
└── tasks.md
```

### Source Code (repository root)

```text
src/SkillCatalog/
├── global.json
├── SkillCatalog.slnx
├── api/
│   ├── SkillCatalog.Api/
│   │   ├── Endpoints/
│   │   ├── Models/
│   │   ├── Options/
│   │   ├── Services/
│   │   └── Program.cs
│   ├── SkillCatalog.Api.Tests/
│   └── SkillCatalog.Api.ContractTests/
└── web/
    ├── src/
    │   ├── api/
    │   ├── app/
    │   ├── components/
    │   ├── features/catalog/
    │   ├── features/skill-detail/
    │   ├── routes/
    │   ├── styles/
    │   └── test/
    ├── e2e/
    ├── package.json
    └── vite.config.ts
```

**Structure Decision**: Isolate the product under `src/SkillCatalog/` so it does not interfere with existing plugins, validator tooling, or fixtures. A nested `global.json` pins .NET 10 LTS independently of the repository's .NET 11 preview tooling.

## Complexity Tracking

No constitution violations or complexity exceptions.
