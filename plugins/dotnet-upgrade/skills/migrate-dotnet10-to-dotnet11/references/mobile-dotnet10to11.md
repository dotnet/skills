# Mobile Platform Breaking Changes (.NET 11)

These breaking changes affect projects that target Android, iOS, tvOS, or Mac Catalyst (`net11.0-android`, `net11.0-ios`, `net11.0-tvos`, `net11.0-maccatalyst`), including .NET MAUI apps and plain .NET for Android or Apple projects. They cover the platform and runtime changes; .NET MAUI UI and control changes are not included. Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/11 (.NET MAUI section)

The main change is that .NET 11 mobile workloads run on CoreCLR instead of Mono. Several settings that tuned Mono now fail the build or silently stop doing anything.

## Build Errors

### CoreCLR replaces the Mono runtime

**Impact: High.** Android, iOS, tvOS, and Mac Catalyst projects use CoreCLR. Setting `<UseMonoRuntime>true</UseMonoRuntime>` fails the build with `NETSDK1242`. There is no opt-out: an app that must stay on Mono stays on .NET 10.

**Fix:** Remove `UseMonoRuntime` from projects that target .NET 11. Don't add code that checks whether the runtime is Mono or CoreCLR; check for the feature the app needs. `RuntimeInformation.FrameworkDescription` now reports ".NET" instead of "Mono", Android packages contain CoreCLR files instead of `libmonosgen-2.0`, and stack traces and diagnostic events name CoreCLR, so update tools or tests that look for Mono names.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/maui/11/mobile-runtime-replacement

### Minimum Android API level raised to 24

**Impact: High (drops device support).** The minimum supported Android version moves from API 21 (Android 5.0) to API 24 (Android 7.0). An existing project that declares a minimum of 21 fails to build (`XA4216`), and apps built with .NET 11 can't be installed on devices running API 21, 22, or 23.

**Fix:** Set `<SupportedOSPlatformVersion>24</SupportedOSPlatformVersion>`, and raise `android:minSdkVersion` in the Android manifest if it is set there. Raising the minimum stops existing users on Android 5.x and 6.x from receiving updates, so confirm the decision with the user before changing it, or keep the app on .NET 10 if those devices must stay supported.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/maui/11/android-minimum-api-level

### Android x86 (32-bit) is not supported

**Impact: Medium.** CoreCLR has no `android-x86` runtime identifier. Builds or emulator tests that use `android-x86` no longer work.

**Fix:** Move emulator tests to x86-64 images and the `android-x64` RID. Keep any target that still needs 32-bit x86 on .NET 10.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/maui/11/android-x86-unsupported

### `MarshalManagedExceptionMode=UnwindNativeCode` is not supported on Apple targets

**Impact: Low.** CoreCLR doesn't support this mode, and the Apple workload reports error 0185.

**Fix:** Remove the setting. Catch managed exceptions before they cross into native code, return a status value or create an Objective-C exception, or use a supported abort policy.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/maui/11/unwind-native-code-apple

### NU1703 for packages with MonoAndroid assets

**Impact: Low–Medium.** With `SdkAnalysisLevel` 11.0.100 or later, restore warns with `NU1703` when a package resolves to deprecated `MonoAndroid` assets instead of `net6.0-android` or later. This fails builds that use `TreatWarningsAsErrors`.

**Fix:** Update to a package version with `net6.0-android` or later assets. If none exists, suppress `NU1703` for that package and plan to replace it.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/11/nu1703-deprecated-monoandroid-framework

## Settings That No Longer Apply

### Mono project properties

**Impact: Medium.** Mono code generation (Mono JIT, Mono AOT, LLVM, and the Mono interpreter) is replaced by the CoreCLR JIT, ReadyToRun, and the CoreCLR interpreter. These properties no longer have their Mono effect; some are ignored, others cause a build warning or error:

- Android: `RunAOTCompilation`, `AotAssemblies`, `EnableLLVM`, `AndroidEnableProfiledAot`, `AndroidAotMode`, `AndroidAotAdditionalArguments`, `AndroidExtraAotOptions`, `@(AndroidAotProfile)`, `AndroidAotEnableLazyLoad`, `AndroidUseInterpreter`, `UseInterpreter`.
- iOS, tvOS, Mac Catalyst: `UseInterpreter`, `MtouchInterpreter`, `EnableSGenConc`, `MtouchEnableSGenConc`, `MtouchUseLlvm`, `MtouchFloat32`, `MonoUseCompressedInterfaceBitmap`, `RunAotCompilation`, `AotAssemblies`, `AotCompileMaxDegreeOfParallelism`.
- `AndroidEnableMarshalMethods` can't be combined with ReadyToRun (`XA1049`).

**Fix:** Remove these properties from .NET 11 projects, or condition them to .NET 10 targets. Use the workload's ReadyToRun defaults rather than translating Mono AOT settings. `MauiEnableFullReadyToRun=true` applies ReadyToRun to the whole Android app; enable it only after measuring startup, memory, and app size. Retest performance after the upgrade.

Sources: https://learn.microsoft.com/en-us/dotnet/core/compatibility/maui/11/mono-project-properties and https://learn.microsoft.com/en-us/dotnet/core/compatibility/maui/11/mobile-code-generation

### Mono runtime settings and environment variables

**Impact: Low–Medium.** CoreCLR ignores `MONO_*` environment variables and launcher settings such as `MONO_LOG_*`, `MONO_PROFILE`, `MONO_GC_*`, `MONO_DEBUG`, and `MONO_THREADS_SUSPEND`. On Android, `debug.mono.*` system properties are being replaced by `debug.dotnet.*`.

**Fix:** Remove the Mono settings. Don't rename `MONO_*` to `DOTNET_*` and assume the same meaning.

Source: https://learn.microsoft.com/en-us/dotnet/core/compatibility/maui/11/mono-runtime-settings

## Diagnostics and Native Interop

**Impact: Low to Medium, depending on tooling.**
- **Trace events:** CoreCLR events replace Mono events, and the `Microsoft-DotNETRuntimeMonoProfiler` provider no longer exists. Tracing needs `EnableDiagnostics=true`.
- **Crash reports:** crash output uses a different format and module names, and no binary dumps are produced. Update crash-collection tooling.
- **Debugging:** Apple mobile debugging moves from the Mono soft debugger to `ICorDebug`. Custom debugger integrations must be rewritten.
- **Mono embedding API:** `mono_*` functions and linking against `libmonosgen-2.0` are not supported, with no direct replacement. Use `UnmanagedCallersOnlyAttribute` and function pointers for native-to-managed callbacks. Normal P/Invoke, `LibraryImport`, Java interop, and Objective-C interop are not affected.

Sources: https://learn.microsoft.com/en-us/dotnet/core/compatibility/maui/11/runtime-trace-events, https://learn.microsoft.com/en-us/dotnet/core/compatibility/maui/11/mobile-crash-reporting, https://learn.microsoft.com/en-us/dotnet/core/compatibility/maui/11/mobile-debugger-interfaces, and https://learn.microsoft.com/en-us/dotnet/core/compatibility/maui/11/mono-embedding-apis
