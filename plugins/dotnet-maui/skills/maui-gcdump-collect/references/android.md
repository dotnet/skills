# Android: physical USB device, Mono .NET 8–10

Source: [.NET 10 Android tracing guide](https://github.com/dotnet/android/blob/release/10.0.1xx/Documentation/guides/tracing.md).
Its “Memory Dumps for Android in .NET 8+” section explicitly supports
`dotnet-gcdump` through dsrouter. Current `main` has changed to .NET 11 CoreCLR;
do not copy its `EnableDiagnostics` recipe into an unverified Mono deployment.

## Preflight

Use a development host with the compatible SDK/workload, Android SDK platform-tools
(`adb`), a USB data cable, USB debugging, an unlocked device and accepted RSA
authorization. `adb devices -l` must show the selected **physical serial** in
`device` state, not `unauthorized`, `offline`, or an emulator.
Every device command below uses that serial. No root or blanket permission changes.

The app must include `libmono-component-diagnostics_tracing.so` beside the Mono
runtime for its target ABI. Inspect the profiling APK/build packaging evidence;
successful build alone does not prove the installed binary is that build.
`AndroidEnableProfiler=true` retains the component for the documented Mono SDKs.
Use the project's actual TFM, not a fixed historical framework copied from a wiki.

## Route and approved preparation

This explicit route avoids the default profiles' port/device ambiguity:

```sh
# Replace the sample serial with the selected physical device.
SERIAL='physical-device-serial'
adb -s "$SERIAL" shell getprop debug.mono.profile
adb -s "$SERIAL" reverse --list
```

Record the previous property and any mapping for device port 9000 before changing
them. If the property is already in use by another profiling session, coordinate
instead of replacing it. It is **device-wide** and can affect other Mono apps.
If a mapping exists, select another free port or obtain permission to replace it.

After approval, in a dedicated host terminal:

```sh
dotnet-dsrouter server-server -ipcs "$PWD/capture-diag.sock" \
  -tcps 127.0.0.1:9001
```

In a second terminal after approval:

```sh
adb -s "$SERIAL" reverse tcp:9000 tcp:9001
adb -s "$SERIAL" shell setprop debug.mono.profile '127.0.0.1:9000,nosuspend,connect'
```

The **device** runtime connects to device loopback 9000; `adb reverse` carries it
to **host** loopback 9001; the router accepts it and exposes host IPC. Do not use
`adb forward` in this connect-mode recipe. Do not mix in the emulator's `10.0.2.2`.
Custom ports must change consistently on both sides.

If a compatible installed app already started with this configuration, leave it
running. Otherwise a rebuild/relaunch is required and must be approved. A build
example (does not install/launch by itself):

```sh
dotnet build App.csproj -f net10.0-android -c Release -p:AndroidEnableProfiler=true
```

Select installation/launch via the existing authorized device workflow using the
same serial; do not issue an unqualified `-t:Run` with multiple devices attached.
Confirm package/build identity and router runtime connection, reproduce the
navigation scenario, then use the main skill's explicit-IPC collection command.
A mobile PID is only corroborating app identity, never the host attach target.

An approved project-specific `AndroidEnvironment` file containing
`DOTNET_DiagnosticPorts=127.0.0.1:9000,nosuspend,connect` is an alternative to the
device-wide property, not an additional conflicting setting. It requires a
rebuild/redeploy. Resolve stale `debug.mono.profile` overrides before using it.

## Troubleshooting and restoration

- Unauthorized/offline: fix cable/unlock/RSA authorization with the operator;
  do not reset all ADB devices or deploy to the first entry.
- No connection: confirm actual installed Mono component, launch-time setting,
  selected serial's reverse rule, host router port/log and app foreground state.
- ADB/device restart removes reverse rules; inspect and restore only this route.
- GC OOM/termination or incomplete graph: stop; report unvalidated capture.

After stopping this router and the approved profiling app session, restore the
previous `debug.mono.profile` value (empty if originally unset) and remove only
the reverse rule this session created:

```sh
adb -s "$SERIAL" shell setprop debug.mono.profile ''
adb -s "$SERIAL" reverse --remove tcp:9000
```

The empty-value example applies **only** when the prior value was empty.
Never use `reverse --remove-all`. Remove the temporary project diagnostic setting
if used, and rebuild an approved ordinary binary before shipping.
