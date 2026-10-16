# macOS requirements by target

Use the project-resolved SDK and workloads, not the latest release by default.
Check host OS/architecture compatibility for the selected SDK and Xcode.

| Requested target | Relevant dependencies |
|---|---|
| Android | `maui-android`, compatible JDK and Android SDK packages |
| iOS | `maui-ios`, compatible full Xcode with iOS SDK |
| Mac Catalyst | `maui-maccatalyst`, compatible Xcode with matching SDK |
| Full MAUI setup | `maui` convenience workload plus dependencies for requested targets |

Workload dependencies bring required platform packs; don't redundantly install
every workload ID. Microsoft OpenJDK is recommended/tested for Android; inspect
the actual selected JDK before judging a different vendor.

Xcode is **not** required for an Android-only check. Standalone Apple Command
Line Tools are not a substitute for full Xcode when building iOS/Mac Catalyst.
Use the corresponding Apple manifest's `xcode.version` and version-specific
release guidance. Simulators are needed for requested simulator deployment,
not every build; device signing is a separate concern.
