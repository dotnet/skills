# Windows scoped diagnostics

For Java selection, inspect `JavaSdkDirectory`, `JAVA_HOME`, and
`Get-Command java`; invoke `bin\java.exe` and `bin\javac.exe` under the build's
actual directory. A vendor substring is not a pass/fail test.

For Android packages, use the resolved SDK path and actual `sdkmanager.bat`:

```powershell
& $SdkManager "--sdk_root=$AndroidSdk" --list_installed
```

For Windows SDK failures, inspect the requested Windows TFM/platform version
and installed SDK components, not Android dependencies.

Emulator acceleration is not required for build-only CI. Do not disable Hyper-V,
edit boot settings or install legacy acceleration drivers during toolchain
diagnosis. Investigate a requested emulator failure separately with its exact
error and current official Android guidance.
