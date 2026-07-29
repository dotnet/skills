# Data Model: Skill Package Upload and Validation

## UploadedPackage

- fileName, mediaType, compressedBytes, uploadRevision
- entries: ArchiveEntry[]
- rules: one `.zip` or `SKILL.md`; bounded size; upload revision changes with every selection

## ArchiveEntry

- normalizedPath, compressedBytes, expandedBytes, mediaType, kind
- rules: relative normalized path, unique case-insensitively, supported root/type, bounded ratio and size, not encrypted or link-like

## ParsedSkill

- plugin, name, description, markdown, resources, referencedPaths, owners, disposition (`new` or `update`)

## ParsedEvaluation

- scenarios, prompts, expectActivation, graders, rubric, fixtures, timeout

## ValidationFinding

- code, severity (`error` or `warning`), location, message, guidance

## ValidatedPackage

- uploadRevision, valid, findings, skill preview, evaluation summary, ownership summary, normalized manifest

## State Transitions

```text
Empty -> FileSelected -> Uploading -> Invalid | ValidWithWarnings | Valid
Any replacement -> FileSelected (all prior results stale)
Valid -> Download request -> Revalidate same bytes -> Normalized ZIP | Current findings
Navigation/refresh -> Empty
```

The server persists none of these states and never extracts content to disk.
