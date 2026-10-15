# Android: physical device, Mono .NET 8–10

Sources: [.NET 10 Android tracing guide](https://github.com/dotnet/android/blob/release/10.0.1xx/Documentation/guides/tracing.md)
(“Memory Dumps for Android in .NET 8+”) and the current `main`-branch guide pinned at
[`e2230e9d`](https://github.com/dotnet/android/blob/e2230e9d5d3e6b1f1f8c25faca1bc2612b8bf116/Documentation/guides/tracing.md)
(.NET 11+ CoreCLR lane; its recipe is otherwise out of scope for this Mono skill).
`$(EnableDiagnostics)` aliasing to `AndroidEnableProfiler=true` is confirmed in
`Microsoft.Android.Sdk.DefaultProperties.targets` from `release/9.0.1xx` onward
(and present on `main`/11+), but **not** on `release/8.0.1xx` — that branch has no
`$(EnableDiagnostics)` reference at all. On .NET 8 set `-p:AndroidEnableProfiler=true`
directly; do not rely on `$(EnableDiagnostics)` there.

## Preflight

Use a development host with the compatible SDK/workload and Android SDK
platform-tools (`adb`). `adb devices -l` must show the selected device in `device`
state, not `unauthorized` or `offline`; a USB cable, wireless `adb` debugging, and
an attached emulator all reach that same state the same way — `adb` does not
record which transport was used, so do not assume or require a USB cable. Neither
`dotnet-dsrouter`'s Android router nor `dotnet-gcdump collect --dsrouter android`
accepts a device-serial flag — both shell out to plain `adb` and rely on the
inherited `ANDROID_SERIAL` environment variable to select one device when more
than one is attached. Export `ANDROID_SERIAL=<selected serial>` (or prefix each
command below with it) before running them if multiple devices/emulators are
attached. No root or blanket permission
changes. (An emulator-only request is still out of this skill's scope — see
`SKILL.md` — because this recipe is only verified against a physical device.)

The app must include `libmono-component-diagnostics_tracing.so` beside the Mono
runtime for its target ABI. Inspect the profiling APK/build packaging evidence;
successful build alone does not prove the installed binary is that build.
`-p:EnableDiagnostics=true` retains the component on `net9.0-android`/`net10.0-android`
and later; on `net8.0-android` use `-p:AndroidEnableProfiler=true` instead, since
`$(EnableDiagnostics)` is not aliased there (see Sources above). Use the project's
actual TFM, not a fixed historical framework copied from a wiki.

## Route and approved preparation

`dotnet-gcdump`'s own `--dsrouter` switch is the default route: it starts an
Android-mode router and sets up the device's `adb reverse` port forward itself,
replacing the previously-documented manual `dotnet-dsrouter server-server` +
`adb reverse` + `setprop debug.mono.profile` sequence. Confirmed locally with the
installed `dotnet-gcdump`/`dotnet-dsrouter` 10.0.750501 (no device attached, so no
device-success claim is made): `dotnet-gcdump collect --dsrouter android` prints
the exact build command to run and then honestly reports
`adb: no devices/emulators found` while attempting that forward. This switch was
absent from 9.0.621003 (the version this skill's earlier Android fixture text
cites) and confirmed present from 9.0.652701 onward; check the installed
`collect --help` before relying on it.

After approval, the capture command (Step 4 of the main skill) becomes (prefix with
`ANDROID_SERIAL=<selected serial>` whenever more than one device/emulator is
attached, since this command has no serial flag of its own):

```sh
ANDROID_SERIAL=<selected serial> dotnet-gcdump collect --dsrouter android \
  --output "$PWD/after-navigation.gcdump" --timeout 60
```

If a compatible installed app already started with diagnostics enabled, leave it
running and reproduce the scenario, then run the command above. Otherwise a
rebuild/relaunch is required and must be approved. A build example (does not
install/launch by itself):

```sh
dotnet build App.csproj -f net10.0-android -c Release -p:EnableDiagnostics=true
```

This resolves to the same defaults `dotnet-gcdump --dsrouter android` expects:
`DiagnosticAddress=127.0.0.1`, `DiagnosticPort=9000`, `DiagnosticSuspend=false`,
`DiagnosticListenMode=connect`. Do not also pass conflicting `Diagnostic*`
properties unless a non-default port/suspend behavior is explicitly required.

Select installation/launch via the existing authorized device workflow using the
same serial; do not issue an unqualified `-t:Run` with multiple devices attached.
Confirm package/build identity, reproduce the navigation scenario, then run the
capture command above. A mobile PID is only corroborating app identity, never the
host attach target.

No manual `adb reverse`, no manual `setprop debug.mono.profile`, and no standalone
`dotnet-dsrouter server-server` invocation are required for this route — they
would only risk conflicting port/property state against what `--dsrouter android`
already manages. If finer-grained control or visible router logs are needed
instead, run the named router mode separately (still not the raw
`server-server -ipcs`/`-tcps` form) and attach by its printed PID:

```sh
dotnet-dsrouter android
# Printed: the same build command above, plus "... pid=<PID>"
dotnet-gcdump ps                     # confirm the router's PID
dotnet-gcdump collect -p <PID> --output "$PWD/after-navigation.gcdump" --timeout 60
```

## Troubleshooting and restoration

- Unauthorized/offline: fix cable/wireless pairing/unlock/RSA authorization with
  the operator; do not reset all ADB devices or deploy to the first entry.
- `adb: no devices/emulators found`: the router could not see the selected device
  at all; reselect/reconnect it before retrying — do not assume a stale
  port-forward is the cause, since this route recreates the forward on every run.
- No connection otherwise: confirm the actual installed Mono component,
  launch-time `EnableDiagnostics`/`Diagnostic*` properties, selected serial,
  router output/log and app foreground state.
- GC OOM/termination or incomplete graph: stop; report unvalidated capture.

Stop the router (Ctrl+C on the `dotnet-gcdump`/`dotnet-dsrouter` process, or the
recorded PID) after capture. Because this route does not set a persistent
device-wide property, there is no `debug.mono.profile` value to restore. Remove
any temporary project diagnostic build settings and rebuild an approved ordinary
binary before shipping.
