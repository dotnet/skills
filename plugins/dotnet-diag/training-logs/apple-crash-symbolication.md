# Training Log: apple-crash-symbolication

## Session: 2025-07-24 — macOS .NET 10.0.4 crash (dotnet/runtime#125513)

**Crash:** `dotnet` process on macOS 15.7.4 ARM64, EXC_BAD_ACCESS/SIGSEGV in libcoreclr.dylib, .NET 10.0.4. GC-vs-thread-startup race in `CallDescrWorkerInternal`. 41 threads, 391 .NET frames.

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
- `.github/skills/apple-crash-symbolication/SKILL.md` — synced with canonical

### Key Learnings

- macOS .NET symbols use a completely different distribution mechanism than iOS (separate `.symbols` NuGet package, flat `.dwarf` format)
- Apple .ips files can have case-conflicting JSON keys — this is an Apple-side quirk, not .NET-specific
- Users frequently want crash analysis beyond "here are the frames" — the skill should support the full triage workflow
