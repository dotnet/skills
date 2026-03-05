# macOS Installation Commands

## Xcode

### Install Xcode

**Do not install Xcode from the App Store** — it can auto-update to a version newer than what .NET MAUI supports.

Download a specific version from [Apple Developer Downloads](https://developer.apple.com/download/all/), matching the `xcode.version` range from WorkloadDependencies.json.

### Install Command Line Tools

```bash
xcode-select --install
```

### List Xcode Installations

```bash
ls -d /Applications/Xcode*.app 2>/dev/null
xcodebuild -version
xcode-select -p
```

### Set Active Xcode Version

```bash
sudo xcode-select -s /Applications/Xcode.app/Contents/Developer
```

### Accept Xcode License

```bash
sudo xcodebuild -license accept
```

### Verify Xcode Installation

```bash
xcodebuild -version
xcrun simctl list devices available
```

---

## iOS Simulators

Only create a simulator if none exist. Prefer a recent iPhone device type with the latest available runtime.

```bash
# Check if any simulators already exist
xcrun simctl list devices available

# If none exist, find the latest iPhone device type and runtime
xcrun simctl list devicetypes | grep iPhone
xcrun simctl list runtimes | grep iOS

# Create one using a chosen device type and runtime from the lists above
# Replace <DEVICE_TYPE_ID> and <RUNTIME_ID> with identifiers from the commands above
xcrun simctl create "My iPhone Simulator" "<DEVICE_TYPE_ID>" "<RUNTIME_ID>"
```
