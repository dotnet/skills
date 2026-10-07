---
name: dotnet-maui-doctor
description: >-
  Diagnose .NET MAUI toolchain setup and build failures: missing SDK/workloads,
  Android SDK not found, Java/JDK version or path errors, Xcode not found, and
  environment verification after updates. Use for MAUI setup plans and scoped
  environment health checks on macOS, Windows, or Linux. Respect project SDK
  pins, target frameworks, and diagnosis-only requests. Not for non-MAUI projects,
  Xamarin.Forms, runtime app crashes, UI code, or app-store signing/publishing.
license: MIT
---

# .NET MAUI Doctor

Diagnose the requested host and target, not every possible MAUI dependency.

## Scope and stop conditions

- Advice-only or supplied-log questions: answer from the evidence; do not run a
  machine inventory, download packages, or create a sample project.
- With shell access, use read-only inspection first. SDK/workload/JDK installs,
  license acceptance, Xcode selection, persistent environment changes, restore,
  builds, and deployment are separate actions requiring the user's authorization.
- An already-working target needs no remediation. Stop after the requested check.
- Do not upgrade project pins to the latest SDK to make an inventory look healthy.
  Maintenance releases are not automatically invalid; report support/lifecycle
  concerns separately from the observed failure.
- Do not invoke this workflow for app logic, runtime crashes, or signing failures.

## Diagnostic path

1. Identify the request, host, project, target framework, and first actionable
   error. Inspect the project and applicable `global.json` (including ancestors),
   imported build settings and CI configuration only as needed.
2. From the project directory, inspect `dotnet --version`, `dotnet --info`, and
   `dotnet workload list` when relevant. Check SDK `version`, `rollForward`,
   `allowPrerelease`, and `workloadVersion`; installed SDKs alone do not establish
   which SDK the project resolves. A missing pinned SDK is a selection problem,
   not evidence that the pin should be removed.
3. Select only relevant checks:

   | Target on host | Checks |
   |---|---|
   | Android on macOS/Windows/Linux | Android workload, actual selected JDK and Android SDK |
   | iOS/Mac Catalyst on macOS | Matching Apple workload, selected Xcode and SDK |
   | Windows on Windows | Windows target framework and Windows SDK |
   | iOS from Windows | Mac build-host requirements; do not claim local Xcode is available |
   | iOS/Mac Catalyst/Windows on Linux | Explain host limitation; do not install unsupported workloads |

   `maui` is a convenient full setup on macOS/Windows, not mandatory for an
   Android-only check. Linux Android setup uses `maui-android`. Plain `android`
   supports .NET for Android; it alone does not establish MAUI dependencies.
4. Use installed/effective workload manifests and project targets for dependency
   requirements. Read `references/workload-dependencies-discovery.md` only when
   exact versions/package discovery is needed. Resolve manifest version **and its
   feature band**, which can differ from the selected SDK's band. Missing metadata
   is an uncertainty, not a license to query an unrelated latest release.
5. For Java errors, read `references/microsoft-openjdk.md`. Microsoft OpenJDK is
   recommended and tested; a different vendor is not by itself a proven failure
   or a blanket compatibility guarantee. Compare the actual build-selected
   `JavaSdkDirectory`, JDK executables/version/architecture, `JAVA_HOME`, and PATH.
   Check `AndroidSdkDirectory` and selected SDK packages similarly.
6. Load just the matching platform requirement or troubleshooting reference for
   the unresolved issue. Installation references are for an authorized fix or a
   requested plan, not compulsory reads on every invocation.

## Remediation and verification

Choose the smallest evidenced fix. Read `references/installation-commands.md`
for project-aware Android dependency installation and workload-set rules.
Preserve repository pins. `workload update` is an intentional version change;
`workload repair` reinstalls installed workload packs for corruption. Neither is
a first response to an unexplained error, nor universally forbidden.

If a build is authorized, use the existing project with its exact target:
`dotnet build <project> -f <project-target-framework>`. Building writes outputs
and can restore packages; do not call it read-only. Do not create an all-platform
template to validate one target. Deployment/emulator launch requires separate
permission and is not needed to establish build success.

## Output contract

Keep small requests concise. Report:

- **Finding:** root cause supported by evidence, or the missing evidence.
- **Next step:** a scoped check or proposed/authorized fix, preserving pins.
- **Validation:** what actually ran and its result; distinguish supplied evidence,
  static inspection, successful target build, and unverified plan. Never claim a
  healthy environment or working app from version strings or simulated fixtures.

Official guidance: [.NET MAUI installation](https://learn.microsoft.com/dotnet/maui/get-started/installation),
[Android dependencies](https://learn.microsoft.com/dotnet/android/getting-started/installation/dependencies),
[workload sets](https://learn.microsoft.com/dotnet/core/tools/dotnet-workload-sets).
