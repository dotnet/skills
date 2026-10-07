# Capability evidence and limits

Research checked 2026-10-07; documentation is not physical-device validation.

| Claim | Authoritative evidence / boundary |
|---|---|
| Entry point and memory-specific guidance | [MAUI profiling](https://github.com/dotnet/maui/wiki/Profiling-.NET-MAUI-Apps), [MAUI Memory Leaks](https://github.com/dotnet/maui/wiki/Memory-Leaks). |
| Direct Android Mono gcdump in .NET 8+ | [Android release/10.0.1xx tracing](https://github.com/dotnet/android/blob/release/10.0.1xx/Documentation/guides/tracing.md#memory-dumps-for-android-in-net-8). Includes component, property, reverse forwarding and host router PID. |
| Android main is a different runtime lane | [Current Android tracing](https://github.com/dotnet/android/blob/main/Documentation/guides/tracing.md) now uses .NET 11 CoreCLR. Never infer Mono support from that quickstart. |
| iOS physical vs simulator | [macios Profiling device](https://github.com/dotnet/macios/wiki/Profiling#device) documents server-client USB forwarding and mlaunch device environment. MAUI memory wiki documents direct gcdump and .NET 9 Release preparation. |
| Diagnostic server and iOS static component | [Mono release/10.0 design](https://github.com/dotnet/runtime/blob/release/10.0/docs/design/mono/diagnostics-tracing.md). Runtime component must be real, not a stub; port set at launch; no Mono core-dump IPC command. |
| IPC connect/listen, local route security | [dotnet-dsrouter](https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-dsrouter). Four explicit modes avoid default-profile ambiguity; endpoints are unauthenticated/unencrypted. |
| Capture/output/timeout/report syntax and impact | [dotnet-gcdump](https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-gcdump). Full GC, potential long pause, event loss or OOM; file report validates parsing without collecting again. |
| Minimum-version command syntax | [9.0.621003 CollectCommandHandler](https://github.com/dotnet/diagnostics/blob/v9.0.621003/src/Tools/dotnet-gcdump/CommandLine/CollectCommandHandler.cs) confirms `--diagnostic-port`, `--output`, `--timeout`, explicit-port connect-only restriction and overwrite behavior. Newer Learn examples are not evidence of older-tool listen support. |
| Physical iOS USB host/selection limits | [USBMuxTcpClientRouterFactory](https://github.com/dotnet/diagnostics/blob/main/src/Tools/dotnet-dsrouter/USBMuxTcpClientRouterFactory.cs) uses Apple CoreFoundation/MobileDevice frameworks and connected USB device notifications. Do not invent a router UDID flag. |

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
