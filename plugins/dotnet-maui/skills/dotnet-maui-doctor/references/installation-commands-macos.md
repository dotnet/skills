# macOS Apple tooling

Load only for an Apple target. Inspect before changing selection:

```bash
xcode-select -p
xcodebuild -version
ls -d /Applications/Xcode*.app 2>/dev/null
```

If an existing compatible Xcode is available, a build-scoped `DEVELOPER_DIR`
can avoid changing the system selection. Global selection changes need approval:

```bash
sudo xcode-select -s "/Applications/<chosen-Xcode>.app/Contents/Developer"
```

For a missing/incompatible version, select the version matching the **effective
Apple workload**, macOS and project requirements. [Apple Developer Downloads](https://developer.apple.com/download/all/)
provides versioned downloads (Apple sign-in may be required). The App Store can
provide Xcode too, but uncontrolled updates can outrun the selected workload;
avoid claiming its Xcode is inherently invalid.

Standalone `xcode-select --install` is not a substitute for full Xcode's iOS SDK.
License acceptance (`sudo xcodebuild -license accept`) needs explicit permission.

Only when simulator use is requested, inspect:

```bash
xcrun simctl list devices available
xcrun simctl list runtimes
```

Create a simulator or download its runtime only if missing and authorized, using
actual supported device/runtime IDs. Never erase all simulators as routine repair.
