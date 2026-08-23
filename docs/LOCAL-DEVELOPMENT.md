# Local development on non-glibc hosts

This repository pins a **.NET 11 preview SDK** in the root `global.json`:

```json
{
  "sdk": {
    "version": "11.0.100-preview.3.26207.106",
    "rollForward": "latestMajor"
  },
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

The official .NET SDK builds distributed from <https://dotnet.microsoft.com> and the
`dotnet-install.sh` script are **glibc** binaries. They will not run on platforms
whose system loader is not glibc. This document records the verified constraint and
the supported ways to develop locally.

## Verified constraint

The following was confirmed on a Termux / Android (aarch64) host:

| Check | Result |
|-------|--------|
| `dotnet` on PATH before install | not found |
| Install `.NET 11 preview` via `dotnet-install.sh --version 11.0.100-preview.3.26207.106` | "Installation finished successfully" |
| Run `./.dotnet/dotnet --version` | `cannot execute: required file not found` |
| ELF interpreter requested by the SDK muxer | `/lib/ld-linux-aarch64.so.1` (glibc) |
| glibc `libc.so.6` present on device | **none** |
| System loader present | `/system/bin/linker64` (Android Bionic) |
| System `dotnet-sdk-10.0` from the distro package | **works** (it is a Bionic build: interpreter `/system/bin/linker64`) |

Conclusion: the SDK tarballs from dotnet.net are unusable on Bionic-only hosts
(Termux on Android, some minimal containers). `patchelf` does not help because the
glibc loader itself is absent. This is a platform ABI limit, not a configuration
problem in `global.json`.

## Recommended local development environments

Use a host that provides glibc. In priority order:

### 1. GitHub Codespaces / GitHub Actions (already used by CI)
The repository's workflows (for example `skill-validator.yml`) use
`actions/setup-dotnet@v5` with `global-json-file: global.json`, which installs the
exact pinned preview on `ubuntu-latest`, `windows-latest`, and `macos-latest`.
This is the source of truth for a green build.

### 2. Docker (Linux, glibc)
```bash
docker run --rm -it -v "$PWD":/repo -w /repo mcr.microsoft.com/dotnet/sdk:11.0-preview
dotnet build eng/skill-validator/SkillValidator.slnx
```
The Microsoft `dotnet/sdk` images are glibc-based and resolve the pinned preview.

### 3. WSL 2 on Windows
```bash
wsl -d Ubuntu
sudo apt update && sudo apt install -y dotnet-sdk-11.0   # or use dotnet-install.sh
cd /mnt/c/path/to/repo
dotnet build
```
WSL2 runs a real glibc Linux kernel/userspace, so the preview SDK works.

### 4. A glibc Linux VM or remote host
Any x64/arm64 Linux with glibc (Ubuntu, Fedora, Debian) can run
`dotnet-install.sh --version 11.0.100-preview.3.26207.106` and build the repo.

## What does NOT work (do not waste time)

- Installing the preview SDK under Termux / Android and expecting it to run.
- `patchelf`-ing the SDK to the Bionic loader — the glibc runtime is missing.
- `rollForward` tweaks in `global.json` — they only change SDK *version* selection,
  not the ABI. They cannot make a glibc binary run on Bionic.

## Why the .NET 10 distro package is not enough here

`dotnet-sdk-10.0` installs and builds `net10.0` projects (for example the
`foundry-agent-webapp` backend), but it does **not** satisfy this repository's
`global.json`, which requires the `.NET 11 preview`. The preview is only published
as a glibc SDK, so this repo cannot be built on a Bionic-only host regardless of
which SDK version is installed.

## See also

- Repository website / dashboard: <https://dotnet.github.io/skills/>
- Skill authoring guide: <https://agentskills.io>
- `CONTRIBUTING.md` for how to add or change a plugin.
