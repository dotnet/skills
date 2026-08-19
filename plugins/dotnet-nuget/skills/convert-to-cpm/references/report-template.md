# CPM Conversion Report

Create `convert-to-cpm.md` with the baseline and converted artifacts in their common artifact directory. The report must be self-contained and suitable for a pull request or team review. Use compact evidence extracted from the package snapshots; do not load raw JSON again solely to write prose.

## 1. Conversion overview

Include:

- Scope and projects converted
- Every explicit project or solution CLI target used for baseline and validation
- Each `Directory.Packages.props` path and the projects governed by that management scope
- Number of unique packages centralized
- Projects or packages skipped, with reasons
- MSBuild version properties inlined, retained, or removed
- Every shared `.props`/`.targets` file inspected or changed, named explicitly (for example, `SharedPackages.props`)
- Conditional references preserved

## 2. Version conflict resolutions

For every conflict, provide:

| Package | Versions and projects | Decision | Impact |
|---------|-----------------------|----------|--------|

State which projects resolve a different version after conversion. If no conflicts existed, say that versions were already consistent.

## 3. Package comparison: baseline vs. result

Use every target's baseline and post-conversion package snapshots to produce two aggregate tables. Deduplicate projects that occur in overlapping targets.

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

Call out `VersionOverride`, removed MSBuild properties, conditional-version changes, and unexplained package differences. Recommend `dotnet test` when it was not run; claim it ran only when the user requested it or the workflow's resolved-version-change rule actually ran it.

Treat intentional major-version alignment as high risk and minor/patch alignment as moderate risk unless stronger project-specific evidence supports another classification. This warning does not require an additional package scan.

If resolved versions changed and tests were run, state the exact test result. If tests failed for a reason not clearly caused by CPM, preserve that distinction and list the failure as follow-up work rather than claiming conversion failure.

## 5. Follow-up items

Use a numbered checklist for applicable items only:

- Security advisories and minimum patched versions
- Deprecated package replacements
- Future alignment where `VersionOverride` preserved differences
- Test validation and release-note review

These are follow-ups, not additional work to perform during the CPM conversion.

## 6. Artifacts and usage

List:

- The single-target or target-keyed baseline and post-conversion binlog pairs for manual MSBuild comparison and troubleshooting
- The single-target or target-keyed baseline and post-conversion package JSON pairs for machine-readable resolved-package comparison
- `convert-to-cpm.md` as the shareable conversion record

End with any user action required before merge.
