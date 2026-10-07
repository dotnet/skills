---
name: maui-gcdump-collect
license: MIT
description: >
  Capture a .gcdump from a confirmed Mono-runtime MAUI app on a physical Android or
  iOS device. USE FOR: that physical-device Mono capture action — device memory
  snapshots, growing managed heap, preparing a profiling build, or diagnostic-port/dsrouter
  connectivity failures during the capture. DO NOT USE FOR (decline without opening this skill): any CoreCLR or NativeAOT app
  or request, including heap snapshots, growing-heap, or GC dump requests (NativeAOT
  does not support gcdump collection regardless of framing; explain the Mono-only
  boundary instead); production/store-distributed (App Store, TestFlight, Play Store,
  enterprise-signed) binaries where attaching a diagnostic port or bypassing platform
  signing/security would be required; existing dump analysis or process/crash dumps
  (dump-collect), CPU traces, native Java/Objective-C heaps, Windows/Mac Catalyst
  apps, emulator/simulator-only requests, or acknowledgments of already completed
  collection.
---

# Collect a MAUI managed heap on a physical device

Produce a host-side `.gcdump` representing the live **managed** object graph.
It is not an OS crash dump, native heap, or proof of a leak. Collection induces
a full GC and can pause or exhaust a memory-constrained app.

## Step 1: Establish scope and permission

Discover the project, `global.json`, target frameworks, configuration, runtime
selection (`PublishAot`, Android runtime choice), installed SDK/workload versions,
and diagnostics tool versions from available files or supplied facts. Ask only
for unavailable device identity, current launch configuration, scenario timing,
output location, and authorization. Do not require paths that can be discovered.

| Target / facts | Decision |
|---|---|
| Physical Android, confirmed Mono, .NET 8–10, diagnostic component and port configured | Use [Android](references/android.md). |
| Physical iOS, confirmed Mono, .NET 9–10 development build with diagnostic component | Use [iOS](references/ios.md) on a Mac. Mono AOT on iOS is **not** NativeAOT. |
| Already running but no diagnostic port/component | Cannot retrofit this session. Offer an approved profiling rebuild/relaunch; capture only after the reproduction is running again. |
| Store/production binary or denied restart/build changes | Stop; do not promise attach or bypass platform signing/security. |
| Older runtime, .NET 11+, unknown runtime/component, or unsupported host | Verify version-specific official SDK documentation first; these recipes are not a blanket support claim. Android main documentation now describes CoreCLR, not this Mono recipe. |
| Existing artifact analysis, CPU/native heap, crash, CoreCLR/NativeAOT, simulator-only | Redirect to the appropriate workflow without applying this device capture procedure. |

Before tool installation, build changes, deployment/relaunch, forwarding/port
activation, or heap collection, obtain approval for the affected action. Advisory
requests remain advisory. Never globally install tools or touch a connected device
merely to validate advice. Do not upload dumps/logs: type names, roots and app
structure may be sensitive. Agree storage/access/retention before capture.

## Step 2: Prepare the host and a compatible test build

- Use host `dotnet-gcdump` and `dotnet-dsrouter`; matched versions at least
  **9.0.621003** cover the documented profile workflow. Check the selected package's
  host runtime requirements, `--version`, `collect --help`, and router mode help.
  If absent, propose an approved local tool manifest or dedicated `--tool-path`
  install with pinned versions, not a global install or automatic workload update.
- Keep the selected SDK/workload, app runtime, and tool versions in the report.
  An SDK on the host does not prove the deployed app's runtime or diagnostics.
- Use a development/testing binary with diagnostics included. Release is useful
  for reproducing optimized behavior, but must explicitly retain diagnostics.
  Disable XAML hot reload for comparable snapshots; do not rewrite app GC behavior.
- Configure `nosuspend` for a running-state capture, not startup `suspend`.
  Port settings take effect at runtime startup, not retroactively.
- Load **only** the applicable platform reference. If it cannot be read, report
  missing guidance and stop before any platform action.

## Step 3: Bind one device and one application to the router

The device uses TCP diagnostics; the host collector uses IPC through the router.
Do **not** pass an `adb pidof`/iOS PID to host `dotnet-gcdump -p`. `dotnet-gcdump ps`
can list the **host router PID**, which is a valid alternate target only after
correlating its endpoint/log with the selected app. Prefer an explicit unique IPC
endpoint to disambiguate concurrent host apps/routers.

Keep both TCP ends on loopback and transport over USB. Router TCP diagnostics
are unauthenticated and unencrypted; never solve connectivity by listening on
`0.0.0.0`, opening a public firewall port, rooting, or weakening signing.
Confirm the device, bundle/package, runtime connection, and selected IPC endpoint
before triggering GC. The router represents one application at a time.

## Step 4: Capture, validate, and clean up

Once the approved app is running at the requested reproduction point:

```sh
# POSIX example; use the platform reference's matching IPC endpoint.
test ! -e "$PWD/after-navigation.gcdump" &&
dotnet-gcdump collect --diagnostic-port "$PWD/capture-diag.sock,connect" \
  --output "$PWD/after-navigation.gcdump" --timeout 60 &&
dotnet-gcdump report "$PWD/after-navigation.gcdump"
```

On Windows use a unique named pipe, not a Unix socket path. The file is written
**on the host**: no `adb pull` or iOS sandbox extraction is needed for this workflow.
Choose a new output name if it already exists: the collector can delete/overwrite
an existing file. The existence check and the `collect` invocation must run as
**one executable unit whose exit/failure propagates** (for example chained with
`&&`, or an `if`/`then`, or a guard that `exit`s before a `;`-joined invocation)
— never as separate commands or separate steps, since nothing then stops the
invocation from running after a failed or skipped check. In tool version
9.0.621003 explicit diagnostic-port collection supports **connect only**; the
`,connect` suffix is required.
Require successful collector exit, a new nonempty file, and a successful report
with plausible type statistics (or a supported local viewer reading it). A renamed
`.nettrace`/`.dmp`, extension alone, or a partial file does not meet the format
contract. Do not use `report -p` to validate: that triggers another collection.

If collection times out or the graph lacks completion/type data, record failure;
check component inclusion, launch-time port, complementary connect/listen modes,
device trust/USB state, router logs and correct endpoint before retrying. On
buffer loss, OOM or OS termination, stop and reassess with the operator rather
than repeatedly inducing GC. A core dump is not a substitute when gcdump is required.

Stop only the router started for this capture (Ctrl+C or its recorded PID).
Restore only the settings/forwarding created by this session as the platform
reference describes. Remove temporary profiling build settings and use an approved
non-diagnostic build before distribution; preserve the agreed artifact securely.

## Output contract

For advice, provide a compact platform-specific runbook, not execution claims.
For execution, report:

- **Status:** ready / blocked / captured / failed / out-of-scope, with reason.
- App/device/runtime/configuration and host SDK/workload/tool versions.
- Exact device TCP ↔ host TCP/IPC route, target-selection evidence, and commands.
- Host artifact path, byte count, collector exit and format-validation evidence.
- Cleanup performed or still required; permission/relaunch and verification gaps.

Without physical device evidence say **“Physical-device capture not verified.”**
Distinguish source/help checks, build checks and actual captures. No device, no
success claim.

## Sources and version boundary

See [capabilities and authoritative references](references/capabilities.md).
The .NET 7 trace/converter route is not this direct-collection workflow; do not
invent legacy conversion support or silently replace the required format.
