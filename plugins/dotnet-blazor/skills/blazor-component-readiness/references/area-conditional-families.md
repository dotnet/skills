# Conditional requirement families

All six `SCF-*` and six `AI-*` rows are part of the 121-ID canonical inventory. Establish each
family's applicability from the confirmed deliverable, inspected source/documents and explicit
support or promotion claims, not nupkg contents alone. Emit every row; use `not applicable`
only when that inspection establishes no in-scope surface, with a concrete scope rationale.
Missing scope confirmation stays unresolved rather than silently omitted.

These families are canonical requirements, not selectable overlays. Applicability does not
add or remove rows from the ordinary full assessment.

| Family | Decide applicability before compliance |
|---|---|
| Scaffolder | Identify the selected or explicitly promoted generator and its output. Repository templates alone do not establish an in-scope promoted scaffolder; distinguish a separate template package from the selected deliverable. Missing required integration does not remove an otherwise applicable generator. |
| AI skill | Inspect source manifests, skill hubs and documentation for skills/plugins offered to consumers to generate or recommend component code. Explicitly promoted consumer skills are applicable even when source-hosted or copied into consumer projects, absent from the nupkg, or not yet contributed upstream. Contribution and other compliance evidence are the next questions, not prerequisites for applicability. |

For scaffolders, evaluate the generated output as a deliverable: `dotnet scaffold` integration
through `dotnet/scaffolding`, not `dotnet new`,
core accessibility/runtime behavior, dependency constraints/pinning, absence of undocumented
arbitrary script execution, and alignment with supported library versions.

For applicable AI skills, inspect the supplied contribution/development records and verify
contribution to `dotnet-blazor` in `dotnet/skills`, repository guidance, update ownership,
dependency constraints, portability, applicable responsible-AI review, and the generated code
itself. Keep each row's missing fact distinct; fluent output is not evidence of security,
accessibility, or compatibility.

Missing private review records are `owner evidence required`; direct unsafe or unsupported output
may be a `gap`.
