# Repository Instructions

This repository contains skill plugins under `plugins/`. Each subdirectory in `plugins/` is an independent plugin (e.g., `plugins/dotnet-msbuild`, `plugins/dotnet`).

## Build

When you modify files in a plugin, check whether that plugin has a `build.ps1` file in its root directory. If it does, run it after making changes to validate and regenerate any compiled artifacts.

```powershell
pwsh plugins/<plugin>/build.ps1
```

**Example:** After editing skills in `plugins/dotnet-msbuild/`, run:

```powershell
pwsh plugins/dotnet-msbuild/build.ps1
```

This validates skill frontmatter and recompiles knowledge lock files. Always commit the regenerated lock files together with your changes.
