# Data Model: Skill Catalog UI

## CatalogSnapshot

Immutable view of repository content: `revision`, `refreshedAt`, `repositoryUrl`, unique `skills`, unique `plugins`, and safe public `diagnostics`. State transitions are `Building -> Ready` or `Building -> Failed`; a ready snapshot is replaced atomically only by another ready snapshot.

## SkillSummary

| Field | Rules |
|---|---|
| id | Required canonical `<plugin>/<skill>` identifier |
| name / displayName | Required directory and human-readable names |
| description | Required when metadata is valid |
| plugin | Required PluginSummary |
| maturity | Experimental, candidate, stable, deprecated, or unknown |
| compatibility / keywords | Deduplicated normalized arrays |
| validationState | Valid, warning, or invalid |
| sourceUrl | Required authoritative source |

## SkillDetail

Extends SkillSummary with bounded untrusted `instructionMarkdown`, allowlisted public `frontmatter`, sorted `resources`, scoped `diagnostics`, and a nullable `downloadUrl`.

## PluginSummary

Contains unique `slug`, display `name`, optional description/version, and derived nonnegative `skillCount`.

## ResourceSummary

Contains normalized skill-relative `path`, kind (instructions/reference/script/asset/other), media type, byte size, previewability, and optional content URL. Preview is limited to safe bounded text.

## CatalogDiagnostic

Contains stable `code`, severity, sanitized message, and optional repository-relative path. It never exposes secrets or absolute server paths.

## SkillPackage

Transient streamed ZIP with sanitized `<plugin>-<skill>-<revision>.zip` name. Entries are regular files beneath one resolved skill root. A generated manifest records skill id, source revision, source URL, and generation time.

## Relationships

- A CatalogSnapshot contains many plugins and skills.
- A plugin groups one or more skills.
- A skill belongs to one plugin and has zero or more resources.
- A package represents exactly one valid skill at one catalog revision.
