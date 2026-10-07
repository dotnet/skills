# Authorized installation and repair

These commands mutate the machine or project outputs. Present them as a plan
unless execution is authorized. License acceptance needs approval too.

## SDK and workloads

Install the SDK resolved by repository policy from
[official .NET downloads](https://dotnet.microsoft.com/download) or the
[dotnet-install script](https://learn.microsoft.com/dotnet/core/tools/dotnet-install-script).
Do not replace a pin just because another major release exists.

For SDKs supporting workload sets (8.0.400 onward), inspect:

```console
dotnet workload config --update-mode
dotnet workload --version
dotnet workload list
```

If `sdk.workloadVersion` exists in the applicable `global.json`, run workload
commands there and let the pin select the set; do not also pass a conflicting
`--version`. Do not leave the repository to bypass the pin.

Without a repository workload pin, an approved set can be made explicit:

```bash
dotnet workload install maui-android --version "$WORKLOAD_VERSION"
```

Use `maui` for a requested full macOS/Windows setup; use only the required workload
for a limited target. Older SDKs can use their version-specific manifests/rollback
mechanism: do not assume `--version` or workload-set discovery exists there.

| Evidence and intent | Appropriate action after approval |
|---|---|
| Missing project workload | `dotnet workload restore <project>` with effective pin/mode understood |
| Missing workload for a known set | Scoped `workload install` with repository pin or explicit set |
| Installed pack corruption | `dotnet workload repair` reinstalls installed packs; not an SDK upgrade |
| Intentional coordinated upgrade | `dotnet workload update --version <approved-set>` when no global pin controls it |
| SDK selection/path mismatch | Correct selection first; reinstalling every workload is not the diagnosis |

Unversioned update can advance workloads. Repair is not an update command.
Neither repairs a wrong JDK/Android directory. Preserve existing update-mode
configuration; do not change default install modes as part of routine diagnosis.

## Project-aware Android dependencies

Prefer the documented
[`InstallAndroidDependencies`](https://learn.microsoft.com/dotnet/android/getting-started/installation/dependencies)
target when the project and Android workload are available. It examines the
project's target API and installs required components, including Java when a
destination is supplied. Run under the project's selected SDK/workload set.

```bash
dotnet build "$PROJECT" -t:InstallAndroidDependencies -f "$ANDROID_TFM" \
  "-p:AndroidSdkDirectory=$ANDROID_SDK" "-p:JavaSdkDirectory=$JDK"
```

```powershell
dotnet build $Project -t:InstallAndroidDependencies -f $AndroidTfm `
  "-p:AndroidSdkDirectory=$AndroidSdk" "-p:JavaSdkDirectory=$Jdk"
```

Use absolute paths, not a literal `~` embedded in an MSBuild property. Add
`-p:AcceptAndroidSdkLicenses=True` only when acceptance has been approved.
Omit Java installation when an existing compatible JDK should be retained.
This target downloads/installs dependencies; it is **not** a read-only query.

## Manual package installation

If the target cannot be used, discover requirements for the **effective**
manifest using `workload-dependencies-discovery.md`, accounting for the project's
target API. Download missing command-line tools from
[Android's official site](https://developer.android.com/studio#command-line-tools-only).
Resolve the real `sdkmanager` path; `latest` is not guaranteed to exist.

```bash
"$SDKMANAGER" --sdk_root="$ANDROID_SDK" --list_installed
# Only after approval, with packages from the resolved requirements:
"$SDKMANAGER" --sdk_root="$ANDROID_SDK" "${PACKAGES[@]}"
```

```powershell
# SDKMANAGER is the actual sdkmanager.bat path on Windows.
& $SdkManager "--sdk_root=$AndroidSdk" --list_installed
& $SdkManager "--sdk_root=$AndroidSdk" @Packages
```

Quote package IDs containing semicolons. Do not install emulator/system images
for build-only CI. Inspect installed packages after installation, then build
only the requested project target if authorized. A package list is not build
validation.
