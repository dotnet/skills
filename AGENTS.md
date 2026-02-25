# Repository Instructions

This repository contains skill components under `src/`. Each subdirectory in `src/` is an independent component (e.g., `src/dotnet-msbuild`, `src/dotnet`).

## Build

When you modify files in a component, check whether that component has a `build.ps1` file in its root directory. If it does, run it after making changes to validate and regenerate any compiled artifacts.

```powershell
pwsh src/<component>/build.ps1
```

**Example:** After editing skills in `src/dotnet-msbuild/`, run:

```powershell
pwsh src/dotnet-msbuild/build.ps1
```

This validates skill frontmatter and recompiles knowledge lock files. Always commit the regenerated lock files together with your changes.
