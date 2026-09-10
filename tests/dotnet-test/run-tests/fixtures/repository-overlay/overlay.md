---
core: dotnet-test/run-tests
binding-revision: "1"
mode: extend
---

# Repository test execution

- Run the unit suite without restoring with
  `pwsh ./eng/test.ps1 -Suite Unit -NoRestore`.
- The script is the supported entry point; do not substitute a direct
  `dotnet test` command.
