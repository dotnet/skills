# CPM Conversion Report

Create `convert-to-cpm.md` beside the baseline and converted artifacts. The report must be self-contained and suitable for a pull request or team review. Use compact evidence extracted from the package snapshots; do not load raw JSON again solely to write prose.

## 1. Conversion overview

Include:

- Scope and projects converted
- Number of unique packages centralized
- Projects or packages skipped, with reasons
- MSBuild version properties inlined, retained, or removed
- Shared `.props`/`.targets` files changed
- Conditional references preserved

## 2. Version conflict resolutions

For every conflict, provide:

| Package | Versions and projects | Decision | Impact |
|---------|-----------------------|----------|--------|

State which projects resolve a different version after conversion. If no conflicts existed, say that versions were already consistent.

## 3. Package comparison: baseline vs. result

Use `baseline-packages.json` and `after-cpm-packages.json` to produce two tables.

**Changes**

| Project | Framework | Package | Before | After | Reason |
|---------|-----------|---------|--------|-------|--------|

Include changed versions, added/removed packages, and `VersionOverride` decisions. If no entries changed, state that the conversion is version-neutral.

**Unchanged**

| Project | Framework | Package | Version |
|---------|-----------|---------|---------|

List unchanged top-level packages compactly without repeating explanatory prose for each row.

## 4. Risk assessment

Choose one level and explain the evidence:

- **Low risk** -- Version-neutral conversion; restore/build succeeded.
- **Moderate risk** -- Intentional patch/minor alignment or limited overrides; name affected projects.
- **High risk** -- Major version changes, unexpected additions/removals, or unresolved validation concerns.

Call out `VersionOverride`, removed MSBuild properties, conditional-version changes, and unexplained package differences. Recommend `dotnet test`; do not claim it ran unless the user requested it and it actually ran.

## 5. Follow-up items

Use a numbered checklist for applicable items only:

- Security advisories and minimum patched versions
- Deprecated package replacements
- Future alignment where `VersionOverride` preserved differences
- Test validation and release-note review

These are follow-ups, not additional work to perform during the CPM conversion.

## 6. Artifacts and usage

List:

- `baseline.binlog` and `after-cpm.binlog` for manual MSBuild comparison and troubleshooting
- `baseline-packages.json` and `after-cpm-packages.json` for machine-readable resolved-package comparison
- `convert-to-cpm.md` as the shareable conversion record

End with any user action required before merge.
