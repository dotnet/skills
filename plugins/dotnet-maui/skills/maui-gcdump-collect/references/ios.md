# iOS: physical USB device, Mono .NET 9–10

Sources: [MAUI Memory Leaks](https://github.com/dotnet/maui/wiki/Memory-Leaks#ios),
[macios device profiling](https://github.com/dotnet/macios/wiki/Profiling#device),
and the [Mono component design](https://github.com/dotnet/runtime/blob/release/10.0/docs/design/mono/diagnostics-tracing.md).
The MAUI wiki has a .NET 9 Release preparation example and direct gcdump example.
This is a development/testing recipe, **not** a guarantee for App Store/TestFlight
or arbitrary already-installed binaries.

## Preflight

Use a Mac with the project's matching iOS workload, compatible Xcode, signed
development/test app for `ios-arm64`, provisioning for the chosen UDID, USB
pairing/trust and Developer Mode where the iOS version requires it. Keep the
device unlocked for launch. No simulator, `iossimulator-*`, `simctl`, `ios-sim`,
or `--launchsim` is a physical-device substitute.

Discover mlaunch from the installed workload, substituting the real project/TFM:

```sh
dotnet build App.csproj -f net10.0-ios -getProperty:MlaunchPath
```

Use the returned full path and its `--help` / `--listdev` to identify the selected
UDID. Discovery does not authorize deployment.

Debug builds normally include profiling support. For Mono Release test builds,
the MAUI wiki uses the **internal, SDK-dependent** `_BundlerDebug=true` property:

```sh
dotnet build App.csproj -f net10.0-ios -r ios-arm64 -c Release -p:_BundlerDebug=true
```

Verify the installed workload honors it and links the **real static**
`diagnostics_tracing` component rather than its stub. The wiki's `.so` description
must not be interpreted as a physical-iOS dynamic library requirement.
If current build/link evidence cannot establish component inclusion, stop before
claiming capture-ready; do not assume Release contains it or ship this setting.
`PublishAot=true` (NativeAOT) is excluded; ordinary Mono AOT is compatible in principle.

## Route and approved launch

This route is the documented device server-client/usbmux pattern. Unlike the
simulator, the runtime **listens** on device loopback and the router connects over
USB using Apple's MobileDevice framework. This tool's forwarding does not provide
a UDID selector: leave only the intended USB iOS device connected for unambiguous
selection; an mlaunch UDID alone does not select the router's USB device.

After approval, in a dedicated host terminal:

```sh
dotnet-dsrouter server-client -ipcs "$PWD/capture-diag.sock" \
  -tcpc 127.0.0.1:9001 --forward-port iOS
```

An app already running with the component and this port can be collected without
relaunching it. Otherwise agree the loss of current heap state and relaunch a
profiling build. Use the discovered mlaunch path, **one exact `.app` path** and
**the chosen UDID**, not a wildcard matching multiple app bundles:

```sh
# Only if installation/relaunch has been approved:
mlaunch --installdev='bin/Release/net10.0-ios/ios-arm64/App.app' --devname='DEVICE-UDID'
mlaunch --launchdev='bin/Release/net10.0-ios/ios-arm64/App.app' --devname='DEVICE-UDID' \
  --wait-for-exit --argument --connection-mode --argument none \
  '--setenv:DOTNET_DiagnosticPorts=127.0.0.1:9001,nosuspend,listen'
```

Replace `mlaunch` with the discovered executable. Do not set the environment
variable merely in the Mac shell: it must reach the **device app at launch**.
Keep `nosuspend` for post-navigation capture. Verify app/build identity and
router/runtime connectivity, reproduce the scenario, then use the main skill's
IPC collection command. The host router PID is an alternative, never the iOS PID.

No HTTP server or custom in-app dump writer is required: the Mono diagnostics
component is the endpoint. No `adb` or LAN/public listener is involved.
Do not bypass trust, Developer Mode, entitlements or app signing to force access.

## Troubleshooting and restoration

No connection: confirm USB trust/unlock, only the chosen device connected,
correct signed app/build, actual component (not stub), device listener port and
complementary `server-client` mode. A working simulator capture proves none of
these device requirements. Diagnose missing frameworks/host support on the Mac;
do not promise this USB implementation on Windows or Linux.

Stop the router and the approved diagnostic app launch after capture. The launch
environment is session-specific: remove it from any persisted launch configuration
and launch the ordinary build without it. Revert `_BundlerDebug` changes or use
the original configuration; rebuild without diagnostics before distribution.
Do not delete a trusted pairing or other sessions' forwarding as “cleanup.”
