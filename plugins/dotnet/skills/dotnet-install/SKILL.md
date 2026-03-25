---
name: dotnet-install
description: >
  Install .NET executables to PATH — like cargo install and go install.
  Use when the user wants to install, manage, or run .NET tools.
  Covers NuGet packages, GitHub repos, and local projects.
---

# dotnet-install

## When to Use

- The user wants to install a .NET tool to their PATH
- The user wants to list, update, remove, or search for .NET tools
- The user wants to run a .NET tool without installing it (npx-style)
- The user needs help setting up dotnet-install itself

## When Not to Use

- The user is managing NuGet package references in a project (use `dotnet add package`)
- The user wants the traditional `dotnet tool install -g` workflow
- The user is working with .NET SDKs or runtimes (use `dotnet-install.sh` from Microsoft)

## Check if dotnet-install is available

```bash
dotnet-install --version
```

If not installed, install it:

```bash
# Option 1: install script (recommended)
curl -sSfL https://github.com/richlander/dotnet-install/raw/refs/heads/main/install.sh | sh

# Option 2: via dotnet tool (bootstrap)
dotnet tool install -g dotnet-install
dotnet-install setup
```

## Install modes

### From NuGet (pre-built, no SDK required)

```bash
dotnet-install --package dotnetsay
dotnet-install --package dotnet-counters@9.0.0
```

### From GitHub (clones and builds)

Requires git and .NET SDK.

```bash
dotnet-install --github richlander/dotnetsay
dotnet-install --github owner/repo@v1.0 --ssh
```

### From local source (builds)

Requires .NET SDK.

```bash
dotnet-install .                    # current directory
dotnet-install src/my-tool          # subdirectory
dotnet-install app.cs               # file-based app (.NET 10+)
```

### Positional args (auto-classified)

```bash
dotnet-install dotnetsay                  # bare name → NuGet
dotnet-install richlander/dotnetsay       # owner/repo → GitHub
dotnet-install ./src/tool                 # local path → source build
```

Use explicit flags (`--package`, `--github`) for
non-interactive and CI environments.

## Subcommands

```bash
dotnet-install ls                  # list installed tools
dotnet-install rm <tool>           # remove a tool
dotnet-install update [tool]       # update one or all tools
dotnet-install search <query>      # search NuGet for tool packages
dotnet-install info <tool>         # show tool details and provenance
dotnet-install outdated            # check for newer versions
dotnet-install run <pkg> [args]    # run without installing (npx-style)
dotnet-install doctor [--fix]      # diagnose and fix PATH/config
dotnet-install env                 # print environment info
dotnet-install config [key] [val]  # view/set configuration
dotnet-install completion <shell>  # shell completion setup
```

## Install directory

Tools are installed to `~/.dotnet/bin/` by default.

| Override | Effect |
|----------|--------|
| `-o <dir>` | Custom output directory |
| `--local-bin` | Use `~/.local/bin/` instead |
| `DOTNET_INSTALL_HOME` env var | Persistent override |

## Configuration

```bash
dotnet-install config                         # show all settings
dotnet-install config tip.quiet true          # suppress PATH tips
dotnet-install config manage-global-tools true  # drain dotnet global tools via doctor
```

## Behavior notes

- NuGet installs handle pointer packages (meta-packages
  that redirect to RID-specific binaries) automatically
- NativeAOT single-file binaries are placed directly in
  the install dir; managed tools use a `_<name>/`
  subdirectory with a symlink or `.cmd` shim
- `run` downloads to a cache, executes, and does not
  install permanently
- Package signatures are verified when present
- Missing prereqs (git, .NET SDK) produce actionable
  error messages with install links
