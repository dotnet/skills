# Training Log: apple-crash-symbolication

## Session: 2025-07-24 — macOS .NET 10.0.4 crash (dotnet/runtime#125513)

**Crash:** `dotnet` process on macOS 15.7.4 ARM64, EXC_BAD_ACCESS/SIGSEGV in libcoreclr.dylib, .NET 10.0.4. NULL signal handler (`_sigtramp → 0x0`) during thread startup in `CallDescrWorkerInternal` — caused by PAL signal handler regression in PR #124308, fixed in .NET 10.0.5 OOB release. 41 threads, 391 .NET frames. (Original analysis incorrectly attributed to GC-vs-thread-startup race; corrected in session 2026-03-13.)

### Issues Found

| # | Severity | Issue | Fix |
|---|----------|-------|-----|
| 1 | ❌ Critical | Step 4 says download `Microsoft.NETCore.App.Runtime.<rid>` for symbols — wrong for macOS. Symbols are in separate `.symbols` package with flat `.dwarf` files. | Rewrote Step 4 item 2 with platform-specific guidance (iOS vs macOS) and `.dwarf` → `.dSYM` conversion |
| 2 | ❌ Critical | No guidance on converting flat `.dwarf` files to `.dSYM` bundles for `atos` | Added conversion commands to SKILL.md Step 4 and reference doc |
| 3 | ⚠️ Medium | Stop signal "Do not trace into source or debug the runtime" blocked legitimate user requests for crash analysis and issue investigation | Softened: present crash analysis by default, allow deeper investigation when asked |
| 4 | ⚠️ Medium | Validation only listed `mono/` paths — misses CoreCLR crashes (`src/coreclr/`) | Added `src/coreclr/` to validation criteria |
| 5 | ⚠️ Medium | Reference doc missing: JSON case-conflicting keys (`vmRegionInfo` vs `vmregioninfo`), `asi` field may be absent | Added "JSON Parsing Gotchas" and "macOS Symbol Packages" sections to reference doc |

### Script Bug Fixes (same session)

- **JSON case-conflict**: Pre-process `.ips` JSON to rename lowercase `vmregioninfo` → `_vmregioninfo_dup` before `ConvertFrom-Json` (line ~122)
- **Strict-mode `asi` access**: Use `$body.PSObject.Properties['asi']` safe check instead of direct `$body.asi` (line ~437)

### Files Changed

- `plugins/dotnet-diag/skills/apple-crash-symbolication/SKILL.md` — Step 4, Validation, Stop Signals
- `plugins/dotnet-diag/skills/apple-crash-symbolication/references/ips-crash-format.md` — macOS symbols, JSON gotchas
- `plugins/dotnet-diag/skills/apple-crash-symbolication/scripts/Symbolicate-Crash.ps1` — 2 bug fixes

### Key Learnings

- macOS .NET symbols use a completely different distribution mechanism than iOS (separate `.symbols` NuGet package, flat `.dwarf` format)
- Apple .ips files can have case-conflicting JSON keys — this is an Apple-side quirk, not .NET-specific
- Users frequently want crash analysis beyond "here are the frames" — the skill should support the full triage workflow

---

## Session: 2025-07-24 (cont.) — Automated version extraction and symbol acquisition

**Problem:** Symbol acquisition was the biggest friction point. The script knew the UUID but not the .NET version, creating a chicken-and-egg: need version to download symbols, but `Find-RuntimeVersion` needed local symbols. Users had to manually figure out the version.

### Issues Found

| # | Severity | Issue | Fix |
|---|----------|-------|-----|
| 1 | ❌ Critical | `Get-ImageTable` discarded full image paths via `GetFileName()` — version info embedded in path was lost | Added `Path` property to image table and propagated through frame objects |
| 2 | ❌ Critical | No path-based version extraction — only UUID matching against local packs | Added `Get-RuntimeVersionFromPath` (regex on shared framework and NuGet pack paths) |
| 3 | ⚠️ Medium | No automated symbol acquisition guidance when dSYMs missing | Script now emits exact `curl` + `unzip` + `.dwarf` → `.dSYM` conversion commands |
| 4 | ⚠️ Medium | ParseOnly output didn't show detected .NET version | Added version tag to library listing in ParseOnly mode |
| 5 | 💡 Low | No RID inference from crash metadata | Added `Get-RidFromPath` with fallback to OS/CPU type inference |

### Script Changes

- **`Get-ImageTable`**: Preserves full `ImagePath` from `usedImages[N].path` (no longer discarded)
- **`Get-ThreadFrames`**: Propagates `ImagePath` to each frame object
- **`Get-RuntimeVersionFromPath`**: Regex extraction from `.../shared/Microsoft.NETCore.App/<version>/...` and `.../host/fxr/<version>/...`
- **`Get-RidFromPath`**: Extracts RID from NuGet-layout paths; falls back to OS/CPU inference from crash metadata
- **Version identification**: Path-based extraction as fast primary method (instant), UUID matching as fallback
- **Symbol acquisition output**: When dSYMs missing + version known → prints copy-pasteable download commands differentiated for macOS vs iOS/tvOS/MacCatalyst
- **ParseOnly**: Now shows `.NET <version>` next to each library

### SKILL.md Changes

- Step 2: Documents automated version detection from crash paths and symbol acquisition command output
- Step 4: Now mentions script-emitted acquisition commands; added full `curl`+`unzip`+conversion example

### Key Learnings

- .ips crash logs embed the .NET version in `usedImages[].path` — e.g., `/usr/local/share/dotnet/shared/Microsoft.NETCore.App/10.0.4/libcoreclr.dylib`
- Path-based version extraction is instant and eliminates the UUID-matching chicken-and-egg problem
- Preview versions also work: `9.0.1-preview.3.24215.6` matched correctly by the regex
- The script should guide users through the full acquisition workflow, not just report what's missing

---

## Session: 2025-07-24 (cont.) — Symbol server correction

**Problem:** Incorrectly added anti-pattern claiming `dotnet-symbol` and the Microsoft symbol server don't serve macOS symbols. User corrected: `dotnet-symbol --symbols <binary>` **does** download `.dwarf` files for macOS .NET binaries.

### Issue

| # | Severity | Issue | Fix |
|---|----------|-------|-----|
| 1 | ❌ Critical | Added false anti-pattern: "❌ Do not use `dotnet-symbol` or the Microsoft symbol server for macOS crashes" — based on incorrect web research | Replaced with positive guidance: `dotnet-symbol --symbols <binary>` works for macOS, downloads `.dwarf` from msdl.microsoft.com |

### Changes

- **SKILL.md (both copies)**: Replaced ❌ anti-pattern with `dotnet-symbol` as option 4 for symbol acquisition
- **Reference doc**: Replaced incorrect "Symbol server note" with `dotnet-symbol` alternative guidance

### Key Learnings

- **Verify claims empirically before adding anti-patterns.** Web search said the symbol server doesn't host macOS symbols — this was wrong. The user confirmed `dotnet-symbol` downloads `.dwarf` files for Mach-O binaries.
- `dotnet-symbol` requires the binary first (e.g., from the main NuGet runtime package), then downloads matching debug symbols. Workflow: download NuGet runtime pkg → `dotnet-symbol --symbols <binary>` → convert `.dwarf` to `.dSYM`.
- This is simpler than hunting for the separate `.symbols` NuGet package and should be the recommended approach.

---

## Session: 2025-07-24 (cont.) — Integrate dotnet-symbol as preferred acquisition method

**Problem:** Script's symbol acquisition guidance only emitted `.symbols` NuGet package commands for macOS. `dotnet-symbol` is simpler and should be the preferred approach.

### Changes

| # | Component | Change |
|---|-----------|--------|
| 1 | Script acquisition block | macOS guidance now shows Option A (`dotnet-symbol`, preferred) and Option B (`.symbols` NuGet, fallback). iOS/tvOS/MacCatalyst unchanged. |
| 2 | SKILL.md Step 2 | Updated acquisition command description to mention `dotnet-symbol` preference |
| 3 | SKILL.md Step 4 | Restructured: `dotnet-symbol` is now item #2 (preferred for macOS), `.symbols` NuGet demoted to item #3 (fallback) |
| 4 | Reference doc | Restructured macOS Symbol Packages section with "Preferred" and "Fallback" subsections + shared `.dwarf` → `.dSYM` conversion section |

### Rationale

`dotnet-symbol` is preferred because:
- Needs only the main runtime NuGet package (which you need anyway for binaries)
- No need to know about the separate `.symbols` package naming convention
- Uses the binary's Mach-O UUID to query the symbol server — exact match guaranteed
- `--symbols` flag explicitly supports `.dwarf` format

The `.symbols` NuGet fallback is preserved for environments where `dotnet-symbol` isn't installed.

---

## Session 5: Automated symbol server download

### Problem

Symbol acquisition was still manual — the script printed instructions but didn't download anything. Users had to copy/paste commands and re-run the script. The android tombstone skill already downloads symbols automatically from the Microsoft symbol server using ELF build IDs.

### Key findings

1. **Microsoft symbol server URL pattern for Mach-O**: `https://msdl.microsoft.com/download/symbols/_.dwarf/mach-uuid-sym-{UUID}/_.dwarf` — empirically verified (HTTP 302 → Azure Blob, correct Mach-O magic, UUID match confirmed)
2. **UUID format**: Lowercase, no dashes — already how `Format-Uuid` normalizes it
3. **Mach-O 64-bit LE magic**: bytes `CF FA ED FE` — used for download validation
4. **`.dwarf` → `.dSYM` conversion** is purely structural: `mkdir -p name.dSYM/Contents/Resources/DWARF && cp file.dwarf name.dSYM/.../name`

### Changes

- **`Symbolicate-Crash.ps1`**: Added three new parameters (`-SymbolCacheDir`, `-SymbolServerUrl`, `-SkipSymbolDownload`), `Get-DebugSymbols` function (HTTP download with Mach-O validation, caching), `Convert-DwarfToDsym` function (bundle creation with UUID verification), and wired both into the main flow after local dSYM search
- **Manual guidance block**: Refactored to only show for libraries still missing after both local search AND symbol server download failed
- **Both SKILL.md copies**: Updated Step 2 flags, Step 4 to document automatic download behavior with fallbacks
- **Reference doc**: Restructured macOS Symbol Packages section — automatic download as primary, `dotnet-symbol` and NuGet as manual fallbacks
- **Symbol cache**: `$TMPDIR/dotnet-crash-symbols/` by default, overridable with `-SymbolCacheDir`

### Test result

End-to-end test with `~/dev/dotnet-2026-03-12-081456.ips`: script automatically downloaded 48 MB `.dwarf` for libcoreclr.dylib (UUID 567bd720d4ad3ff3b39c1982b66649e5), converted to `.dSYM`, symbolicated all 6/6 .NET frames on crashing thread including `CallDescrWorkerInternal`.

### Rationale

Mirrors the android tombstone skill's `Get-DebugSymbols` pattern — direct HTTP to symbol server using the binary's unique ID, no external tool dependencies. The skill now achieves fully automated symbolication for any published .NET runtime release without requiring `dotnet-symbol`, NuGet packages, or user intervention.

---

## Session: 2026-03-13 — Crash analysis triage order

**Problem:** In an earlier session symbolicating [dotnet/runtime#125513](https://github.com/dotnet/runtime/issues/125513), the skill produced correct symbolication across all 41 threads but the subsequent analysis reached the wrong root cause. The analysis focused on cross-thread GC state (neighboring TP Worker threads blocked in `gc_heap::try_allocate_more_space`) and concluded it was a GC-vs-thread-startup race. The actual cause was `_sigtramp` jumping to address `0x0` (NULL signal handler) — visible in frames #0–#1 of the crashing thread but never examined first.

A second session with a clean context path (skill loaded, symbols auto-downloaded, crashing thread only) correctly identified the `_sigtramp → NULL` pattern and matched it to [dotnet/runtime#125484](https://github.com/dotnet/runtime/issues/125484) — PAL signal handlers reset to NULL under debugger launch on macOS, fixed in the .NET 10.0.5 OOB release.

### Root cause of misdiagnosis

1. **No triage order guidance.** Step 3 listed what to check (asi, version, unsymbolicated frames) but not the order in which to analyze a symbolicated crash. The agent examined cross-thread context before understanding the faulting mechanism.
2. **Context exhaustion.** The earlier session spent most turns fixing tooling issues (skill discovery, symbol formats, script bugs). By the time analysis began, reasoning quality had degraded.

### Change

Added triage order guidance to Step 3: "Start with the faulting mechanism — explain what frames #0 and #1 on the crashing thread mean before examining other threads. Cross-thread context is useful for validation but is not evidence of causation."

### Rationale

This is a behavioral nudge, not a crash-pattern catalog. The skill shouldn't try to enumerate every known crash signature (e.g., `_sigtramp → NULL`). Instead, it should enforce the discipline of examining the faulting mechanism first, which would have led to the correct diagnosis regardless of the specific pattern.

### Key Learnings

- Tooling friction in a session degrades downstream analysis quality. The automated symbol download added in Session 5 directly enabled the second session's clean path to the correct answer.
- Full-thread symbolication is valuable but creates a risk of correlation-not-causation errors if the agent doesn't have clear triage order guidance.
- The retrospective is documented at: [dotnet/runtime#125513 correction comment](https://github.com/dotnet/runtime/issues/125513#issuecomment-4054254202).
