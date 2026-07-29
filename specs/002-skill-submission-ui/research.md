# Research: Skill Package Upload and Validation

## Upload-only workflow

**Decision**: Replace in-browser authoring with upload, validation, preview, and normalized download. Corrections happen in the contributor's editor.

**Rationale**: Repository contributors already work with files; upload-first reduces UI complexity and prevents a second source of truth.

## Archive handling

**Decision**: Stream bounded ZIP entries in memory, inspect metadata before reading content, normalize every path, reject traversal, duplicates, encryption, link-like entries, excessive ratios, and unsupported roots. Never extract to disk.

**Rationale**: Prevents archive traversal and decompression abuse while keeping requests stateless.

## Single Markdown handling

**Decision**: Accept `SKILL.md` as a validation-only single-file package and report missing repository context or evaluation/ownership artifacts as findings.

**Rationale**: Provides a fast first check without pretending a single file is a complete repository contribution.

## Revision and download

**Decision**: Fingerprint selected bytes in the browser. Normalized download resends and revalidates the same file because the server stores no upload state.

**Rationale**: Guarantees download corresponds to the displayed revision without server persistence.
