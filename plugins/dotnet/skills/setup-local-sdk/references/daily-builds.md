# Daily builds (nightlies): channels, feeds and workloads

Read this before installing workloads on an SDK installed with `--quality daily`. A released
preview or RC (`--quality preview`) has its manifests and packs on nuget.org and needs none of it.
Every command below runs through the local `./.dotnet/dotnet` (`.\.dotnet\dotnet.exe` on Windows).

## 1. The channel, the version and the band

A daily *channel* is the name of a build lane of [dotnet/dotnet](https://github.com/dotnet/dotnet):
`11.0.1xx-rc2` (the RC 2 release branch), `11.0.1xx` (the RTM release branch), `12.0.1xx` (`main`,
branded for the next major). The
[builds table](https://github.com/dotnet/dotnet/blob/main/docs/builds-table.md) lists channels but
lags behind branding changes: in October 2026 it still called `11.0.1xx` "main" after `main` had
moved to `12.0.1xx`. Resolve a channel to the build it serves without downloading: the install
script's `--dry-run` (`-DryRun` in PowerShell) prints the payload URL, which carries the version. A
HEAD request on the aka.ms archive link gives the same answer, since the link redirects to that URL.

```bash
bash dotnet-install.sh --channel <CHANNEL> --quality daily --dry-run
# dotnet-install: URL #0 - aka.ms: .../Sdk/11.0.100-rc.2.26509.113/dotnet-sdk-11.0.100-rc.2.26509.113-osx-arm64.tar.gz
curl -sIL -o /dev/null -w '%{url_effective}\n' "https://aka.ms/dotnet/<CHANNEL>/daily/dotnet-sdk-linux-x64.tar.gz"
```

The URL must contain `/Sdk/<version>/`. An unknown channel is not a 404: its aka.ms link lands on a
search page, so no such URL means no such channel.

Read the version, not the channel name: `rc.2` is the release candidate lane, `rtm` the release
branch, `alpha.1` the next major. The SDK's *band* is the version without the build number
(`11.0.100-rc.2.26509.113` is band `11.0.100-rc.2`); workload manifest packages are named after it
(`Microsoft.NET.Sdk.iOS.Manifest-11.0.100-rc.2`).

## 2. Why a plain `workload install` goes wrong on a daily

| Symptom | Cause |
|---|---|
| `No workload update found`, then packs of an older preview (iOS `26.5.11720-net11-p6` on an RC 2 SDK) | nuget.org has no manifests for a daily band, and .NET 10+ SDKs look for *workload sets* by default, which dailies do not publish; the installer falls back to the manifests bundled with the SDK, which can be months old |
| `Version 10.0.13 of package microsoft.netcore.app.runtime… is not found in NuGet feeds` | The `*-net<previous>` compat workloads reference the previous major's servicing runtime, on nuget.org only from Patch Tuesday |
| `Version 36.1.118 of package microsoft.android.sdk.darwin is not found` (or an iOS `net10.0_26.5` pack) | Release builds of dotnet/android and dotnet/macios land on isolated `darc-pub-*` feeds that only dotnet/maui's `NuGet.config` names |
| `Successfully installed workload(s)`, but `workload list` shows `…-net11-rc.1/11.0.100-rc.1` on a `12.0.100-alpha.1` SDK | The same fallback: the install succeeded with the wrong band |

## 3. Procedure

1. **Feeds.** The daily sources must be in the `NuGet.config` that applies to the project. Projects
   built with the daily need them too: their restore pulls `Microsoft.NETCore.App.Ref` and the
   runtime packs of the daily version, which are not on nuget.org (NU1102 otherwise). If the
   repository already has a `NuGet.config`, add the sources to it and keep everything else: existing
   sources, credentials, and `packageSourceMapping`. `./.dotnet/dotnet nuget add source <url>
   --name <name> --configfile <existing file>` adds a source without touching other settings, but
   it never writes mappings: when the file has a `<packageSourceMapping>` section, add a
   `<packageSource key="<name>">` entry for each new source by hand, or restores that use mappings
   never look at it. Scope each entry to what the feed serves, never a catch-all `*`, which would
   make every dependency eligible from every feed. For `dotnet<major>`, the families a daily
   restore pulls: `Microsoft.NETCore.App.*`, `Microsoft.AspNetCore.App.*`,
   `Microsoft.WindowsDesktop.App.*`, `Microsoft.NETCore.DotNetAppHost*`,
   `Microsoft.NETCore.DotNetHost*`, `Microsoft.NET.Sdk.*`, `Microsoft.NET.Workload.*`,
   `Microsoft.NET.Runtime.*`, `Microsoft.NET.ILLink.*`, `Microsoft.DotNet.ILCompiler*`,
   `runtime.*` (the RID-specific runtime and ILCompiler packs) and `Microsoft.Maui.*`. For the
   android feeds, `Microsoft.Android.*`; for the macios feeds, `Microsoft.iOS.*`,
   `Microsoft.MacCatalyst.*`, `Microsoft.macOS.*` and `Microsoft.tvOS.*`. The file is `NuGet.Config`,
   `NuGet.config` or `nuget.config`, whichever the repository has, since NuGet reads all three on a
   case-sensitive file system. Only create the standalone file below, with its `<clear />`, when
   the repository has none. Sources: the `dotnet<major>`
   feed, plus the `darc-pub-*` feeds listed in dotnet/maui's `NuGet.config` on the branch of the band:
   `release/<major>.0.1xx-<label without the dot>` for a preview or rc band (`release/11.0.1xx-rc2`),
   `net<major>.0` for a release band, `main` for an alpha. For an alpha, read the previous major's
   newest release branch as well: `main` may not have moved to the new major yet, while the alpha's
   iOS and Android workloads still carry the previous major's packs from those feeds.

   ```bash
   curl -fsSL "https://raw.githubusercontent.com/dotnet/maui/release/11.0.1xx-rc2/NuGet.config" \
     | grep -o 'https://pkgs.dev.azure.com/dnceng/public/_packaging/darc-pub-dotnet-\(android\|macios\)-[^"/]*/nuget/v3/index.json'
   ```

   ```xml
   <configuration>
     <packageSources>
       <clear />
       <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
       <add key="dotnet11" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet11/nuget/v3/index.json" />
       <!-- plus each darc-pub-dotnet-android-* and darc-pub-dotnet-macios-* feed found above -->
     </packageSources>
   </configuration>
   ```

2. **Manifests, not workload sets.**

   ```bash
   ./.dotnet/dotnet workload config --update-mode manifests
   ./.dotnet/dotnet workload install <ids>
   ```

   With the feeds configured, the installer now fetches the newest `<id>.Manifest-<band>` packages
   and installs their packs. Verified on a `12.0.1xx` daily in October 2026: `ios` and `android`
   resolved to the band's manifests (`27.0.12277-net12-p1`, `37.99.0-preview.1.145`) and installed.

3. **If it fails on a `<previous>.0.x` package that is not found**, the compat manifests point at an
   unreleased servicing runtime. Either wait for Patch Tuesday, or: run `./.dotnet/dotnet workload update`
   so the band's manifests are in place, change that version to the newest public `<previous>.0.x` in
   `.dotnet/sdk-manifests/<band>/microsoft.net.workload.mono.toolchain.net<previous>/*/WorkloadManifest.json`
   and in the sibling `microsoft.net.workload.emscripten.net<previous>` manifest, delete
   `.dotnet/metadata/workloads/<architecture>/<band>/InstallState/default.json` if it exists
   (`<architecture>` is the process architecture, `Arm64` or `X64`), and install with
   `--skip-manifest-update`. Those packs only serve builds for the previous major from the new SDK.
   Tell the user this is a local edit of a throwaway SDK. Verified on an `11.0.1xx-rc2` daily in
   October 2026: after the edit, `maui 11.0.0-rc.2.26505.5/11.0.100-rc.2` installed.

4. **Check the band and the lane, not only the exit code.** `./.dotnet/dotnet workload list` must
   show each requested workload from the SDK's band (`maui 11.0.0-rc.2.…/11.0.100-rc.2`), but it lists
   only the workloads you asked for: `maui` brings in the Android, iOS and Mac Catalyst manifests
   without a row of their own. So check the lane on disk too: in `.dotnet/sdk-manifests/<band>/`,
   the highest version folder of every manifest id (the one the resolver takes) must carry the
   band's label (`37.2.0-rc.2.84`, `27.0.12212-net11-rc.2`, `11.0.100-rc.2.26508.107`). Several lanes
   publish into one band and the installer takes the highest version: on that RC 2 daily,
   `workload update` picked Android `37.99.0-preview.1.4` (a `main` build) over the band's
   `37.2.0-rc.2.x`, and an iOS `27.1.…-xcode27.1-net11.0` build over `27.0.12212-net11-rc.2`,
   while `workload list` showed only a correct `maui` row.
   If the wrong lane won, place the manifest by hand: download the `<id>.Manifest-<band>` package of
   the right build and extract its `data/` folder into `.dotnet/sdk-manifests/<band>/<id>/<version>/`.
   Then delete the other version folders of that id, the wrong-lane one included: the resolver takes
   the highest version folder, so a higher wrong-lane folder left in place wins again. Delete
   `.dotnet/metadata/workloads/<architecture>/<band>/InstallState/default.json` too if it exists, since
   it pins manifest versions, and install with `--skip-manifest-update`.

5. **Build servers.** After manifests or packs changed, run `./.dotnet/dotnet build-server shutdown`.
   MSBuild nodes and the compiler server outlive a build and keep the manifests they loaded; the next
   build would otherwise import the targets of a manifest version that no longer exists.

6. **Report truthfully.** A workload that no manifest of the band defines (no `maui` for a `12.0.1xx`
   alpha while dotnet/maui builds for the previous major, from its `net11.0` and `release/11.0.1xx-*`
   branches) cannot come from that lane: say so, install the rest,
   and point out that the SDK is usable without workloads. Never report a workload as installed when
   `workload list` shows a band older than the SDK's.
