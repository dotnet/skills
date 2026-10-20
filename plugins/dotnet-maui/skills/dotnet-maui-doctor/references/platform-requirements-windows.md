# Windows requirements by target

| Requested target | Relevant dependencies |
|---|---|
| Android | `maui-android`, compatible JDK and Android SDK packages |
| Windows | MAUI Windows components (`maui-windows`), target-compatible Windows SDK |
| Android and Windows/full setup | `maui` is a convenient umbrella |
| iOS | Compatible Mac build host and Apple tooling; no local Windows Xcode |

Respect project SDK/workload pins, host architecture and Windows version.
Resolve the actual Windows target framework/platform version and project
Windows App SDK dependencies, not an unspecified "current" version.
Do not install Android/JDK for Windows-only validation.

Microsoft OpenJDK is recommended/tested for Android. `sdkmanager.bat` is the
Windows command-line tool; quote its path and package IDs. Emulator acceleration
is separate from headless builds and must not trigger OS/hypervisor changes
during environment diagnosis.
