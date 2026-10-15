# Capability evidence and limits

Research checked 2026-10-07; documentation is not physical-device validation.

| Claim | Authoritative evidence / boundary |
|---|---|
| Entry point and memory-specific guidance | [MAUI profiling](https://github.com/dotnet/maui/wiki/Profiling-.NET-MAUI-Apps), [MAUI Memory Leaks](https://github.com/dotnet/maui/wiki/Memory-Leaks). |
| Direct Android Mono gcdump in .NET 8+ | [Android release/10.0.1xx tracing](https://github.com/dotnet/android/blob/release/10.0.1xx/Documentation/guides/tracing.md#memory-dumps-for-android-in-net-8). Includes component, property, and host router PID. `$(EnableDiagnostics)` is confirmed to alias `AndroidEnableProfiler` in the SDK's own targets, so it is safe to use here too (see next row). |
| `EnableDiagnostics`/`dotnet-gcdump --dsrouter` evidence, pinned | [Android tracing @ `e2230e9d`](https://github.com/dotnet/android/blob/e2230e9d5d3e6b1f1f8c25faca1bc2612b8bf116/Documentation/guides/tracing.md) (a `main` snapshot, pinned because `main` moves forward — it was .NET 11+ CoreCLR at this commit). Confirms `$(EnableDiagnostics)`'s role; its own CoreCLR-only command examples are otherwise out of scope here. Locally run `dotnet-gcdump`/`dotnet-dsrouter` 10.0.750501 `--dsrouter android`/`ios` confirm the switch self-manages the router and device port-forward (absent in 9.0.621003, present by 9.0.652701). |
| iOS physical vs simulator | [macios Profiling device](https://github.com/dotnet/macios/wiki/Profiling#device) documents USB forwarding. MAUI memory wiki documents direct gcdump and .NET 9 Release preparation. |
| Diagnostic server and iOS static component | [Mono release/10.0 design](https://github.com/dotnet/runtime/blob/release/10.0/docs/design/mono/diagnostics-tracing.md). Runtime component must be real, not a stub; port set at launch; no Mono core-dump IPC command. |
| IPC connect/listen, local route security | [dotnet-dsrouter](https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-dsrouter). `android`/`android-emu`/`ios`/`ios-sim` named modes (and the lower-level `server-server`/`server-client`/etc. modes for finer control) avoid default-profile ambiguity; endpoints are unauthenticated/unencrypted. |
| Capture/output/timeout/report syntax and impact | [dotnet-gcdump](https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-gcdump). Full GC, potential long pause, event loss or OOM; file report validates parsing without collecting again. |
| Minimum-version command syntax | [9.0.621003 CollectCommandHandler](https://github.com/dotnet/diagnostics/blob/v9.0.621003/src/Tools/dotnet-gcdump/CommandLine/CollectCommandHandler.cs) confirms `--diagnostic-port`, `--output`, `--timeout`, explicit-port connect-only restriction and overwrite behavior. Newer Learn examples are not evidence of older-tool listen support. |
| Physical iOS USB host/selection limits | [USBMuxTcpClientRouterFactory @ `13357e7e`](https://github.com/dotnet/diagnostics/blob/13357e7ed3627ae302283e96142f6eed1e389f7a/src/Tools/dotnet-dsrouter/USBMuxTcpClientRouterFactory.cs) (pinned `main` snapshot) uses Apple CoreFoundation/MobileDevice frameworks and connected USB device notifications. Do not invent a router UDID flag. |

Supported recipe baselines: confirmed Mono .NET 8–10 on physical Android;
Mono .NET 9–10 on physical iOS using a Mac and diagnostic development/test build.
These are source-backed configurations, not a promise that every servicing
release, build pipeline, OS revision or diagnostic tool combination works.
Record concrete versions and check selected-tool help and installed-build evidence.
No physical Android or iOS capture was performed while authoring this skill.

Legacy .NET 7 Android documentation describes collecting specific Mono events in
`.nettrace` and converting with a separate tool. That is not direct gcdump support,
and this skill does not install or claim validation of the legacy converters.
Native Java/Objective-C memory and native handles are not a complete part of the
managed graph; a valid gcdump does not account for all resident device memory.
