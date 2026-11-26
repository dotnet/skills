# iOS: physical USB device, Mono .NET 9–10

Sources: [MAUI Memory Leaks](https://github.com/dotnet/maui/wiki/Memory-Leaks#ios),
[macios device profiling](https://github.com/dotnet/macios/wiki/Profiling#device),
[macios build properties](https://github.com/dotnet/macios/blob/85f8963dc94f8833d0cf6165ac991a0c791c1035/docs/building-apps/build-properties.md),
[`Xamarin.Shared.props`](https://github.com/dotnet/macios/blob/482d490bf45161baded9aa67611b6e3678c6f35b/msbuild/Xamarin.Shared/Xamarin.Shared.props#L224-L245)
(`DiagnosticConfiguration` MSBuild property → `EnableDiagnostics`/`DOTNET_DiagnosticPorts`),
and the [Mono component design](https://github.com/dotnet/runtime/blob/release/10.0/docs/design/mono/diagnostics-tracing.md).
This is a development/testing recipe, **not** a guarantee for App Store/TestFlight
or arbitrary already-installed binaries.

## Preflight

Use a Mac with the project's matching iOS workload, compatible Xcode, signed
development/test app for `ios-arm64`, provisioning for the chosen UDID, USB
pairing/trust and Developer Mode where the iOS version requires it. Keep the
device unlocked for launch. No simulator, `iossimulator-*`, `simctl`, `ios-sim`,
or `--launchsim` is a physical-device substitute.

List candidate devices with the installed SDK's own device discovery, substituting
the real project/TFM, rather than assuming a single attached device:

```sh
dotnet run --project App.csproj -f net10.0-ios --list-devices
```

Use the printed UDID. Discovery does not authorize deployment.

Debug builds normally include profiling support (diagnostics is on by default when
`MtouchDebug`/`MmpDebug` is enabled). For Mono Release test builds, the officially
documented, version-independent property across current .NET SDKs is
**`EnableDiagnostics=true`** (`dotnet/macios`'s `build-properties.md`); it is what
actually gates linking the `diagnostics_tracing` Mono component into the build,
and it implicitly underlies each `Diagnostic*` property used below:

```sh
dotnet build App.csproj -f net10.0-ios -r ios-arm64 -c Release -p:EnableDiagnostics=true
```

If `EnableDiagnostics` is ever unavailable on the installed workload, fall back to
setting one of the `Diagnostic*` MSBuild properties directly (for example
`DiagnosticPort`), which implicitly enables it.

Verify the installed workload honors it and links the **real static**
`diagnostics_tracing` component rather than its stub. If current build/link
evidence cannot establish component inclusion, stop before claiming
capture-ready; do not assume Release contains it or ship this setting. `PublishAot=true` (NativeAOT) is excluded
— CoreCLR always supports diagnostics, NativeAOT never does; ordinary Mono AOT is
compatible in principle.

## Route and approved launch

`dotnet-gcdump`'s own `--dsrouter` switch is the default route: it starts an
iOS-mode router over Apple's MobileDevice/usbmux framework itself, replacing the
previously-documented manual `dotnet-dsrouter server-client -ipcs ... -tcpc ...
--forward-port iOS` invocation. Confirmed locally with the installed
`dotnet-gcdump`/`dotnet-dsrouter` 10.0.750501 (no device attached, so no
device-success claim is made): `dotnet-gcdump collect --dsrouter ios` prints the
exact build/launch properties to use and then honestly reports
`Failed to connect device over USB, no device currently connected`. This tool's
forwarding does not provide its own UDID selector: leave only the intended USB
iOS device connected for unambiguous selection — a `dotnet run --device` UDID
only selects the launch target below, not the router's USB device.

An app already running with the component and the default diagnostic port can be
collected without relaunching it:

```sh
dotnet-gcdump collect --dsrouter ios \
  --output "$PWD/after-navigation.gcdump" --timeout 60
```

Otherwise agree the loss of current heap state and relaunch a profiling build.
Use the project's own run command with the chosen UDID, **not** a wildcard
matching multiple app bundles. The current .NET SDK's `dotnet run` replaces the
previously-documented `mlaunch --installdev`/`--launchdev` sequence. Pass the
device-side diagnostic configuration as an **MSBuild property** (`-p:`), **not**
a runtime environment variable (`-e`/`-e:`): confirmed in `dotnet/macios`'s
`Xamarin.Shared.props`, the build consumes the `DiagnosticConfiguration` MSBuild
property and maps it to the `DOTNET_DiagnosticPorts` environment variable that is
actually forwarded to the device app at launch; a `-e DiagnosticConfiguration=...`
runtime variable is forwarded to the device **unchanged** (under its literal,
wrong name) and never reaches `DOTNET_DiagnosticPorts`, so no listener is
established. Escape the comma as `%2c` because `dotnet run` itself parses an
unescaped `,` as an argument separator:

```sh
# Only if installation/relaunch has been approved. Replace DEVICE-UDID.
dotnet run --project App.csproj -f net10.0-ios -c Release --device DEVICE-UDID \
  -p:DiagnosticConfiguration=127.0.0.1:9000%2cnosuspend%2clisten
```

Setting the `DiagnosticConfiguration` MSBuild property this way also implicitly
sets `EnableDiagnostics=true` for this build (same `Xamarin.Shared.props`: it sets
`EnableDiagnostics` to `true` whenever `DiagnosticConfiguration` — or any other
`Diagnostic*` property — is non-empty and `EnableDiagnostics` was not already set),
so it links the component into this rebuild even for a Release project with no
persisted diagnostics setting; passing the earlier command-line-only
`-p:EnableDiagnostics=true` again is unnecessary but harmless. If retaining a
separately-built component is preferred instead of this rebuild, pass
`-p:EnableDiagnostics=true` to build/retain it and set the actual runtime
variable directly as `-e DOTNET_DiagnosticPorts=127.0.0.1:9000%2cnosuspend%2clisten`.

Port **9000** matches `--dsrouter ios`'s own usbmux connection target (confirmed in
`dotnet-dsrouter` source: unlike Android's `adb reverse`, the iOS router applies no
port offset). Using 9001 here would make the app unreachable by the default route.

Keep `nosuspend` for post-navigation capture. Verify app/build identity and
router/runtime connectivity, reproduce the scenario, then run the capture command
above.

If the app is already listening on a **non-default** port (for example 9001, with
no relaunch permitted), `--dsrouter ios` cannot reach it — start the explicit
router instead and connect by its IPC path, never by guessing a device port:

```sh
dotnet-dsrouter server-client -ipcs "$PWD/capture-diag.sock" \
  -tcpc 127.0.0.1:9001 --forward-port iOS &
dotnet-gcdump collect --diagnostic-port "$PWD/capture-diag.sock",connect \
  --output "$PWD/after-navigation.gcdump" --timeout 60
```

(the same explicit `server-client`/`--diagnostic-port` pattern applies to Android
for a non-default port; see the Android reference). The host router PID is an
alternative, never the iOS PID.

No HTTP server or custom in-app dump writer is required: the Mono diagnostics
component is the endpoint. No `adb` or LAN/public listener is involved.
Do not bypass trust, Developer Mode, entitlements or app signing to force access.

## Troubleshooting and restoration

No connection: confirm USB trust/unlock, only the chosen device connected,
correct signed app/build, actual component (not stub), device listener port and
`--dsrouter ios` mode. A working simulator capture proves none of these device
requirements. Diagnose missing frameworks/host support on the Mac; do not promise
this USB implementation on Windows or Linux.

Stop the router and the approved diagnostic app launch after capture. The launch
environment is session-specific: remove it from any persisted launch configuration
and launch the ordinary build without it. Revert `EnableDiagnostics` (or any
`Diagnostic*` property) changes or use the original configuration; rebuild
without diagnostics before distribution.
Do not delete a trusted pairing or other sessions' forwarding as “cleanup.”
