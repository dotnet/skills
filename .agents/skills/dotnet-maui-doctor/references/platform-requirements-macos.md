# macOS Platform Requirements

## Required Components

| Component | Requirement | Notes |
|-----------|-------------|-------|
| macOS | Recent version | ARM64 or Intel |
| .NET SDK | Active support | Query releases-index.json for latest |
| Xcode | Per WorkloadDependencies | From [Apple Developer Downloads](https://developer.apple.com/download/all/) |
| Command Line Tools | Match Xcode | `xcode-select --install` |

## Required Workloads

| Workload | Required | Purpose |
|----------|----------|---------|
| `maui` | ✅ Yes | Core MAUI framework |
| `android` | ✅ Yes | Android targets |
| `ios` | ✅ Yes | iOS targets |
| `maccatalyst` | Recommended | Mac Catalyst targets |

## Android Development

| Component | Source | Notes |
|-----------|--------|-------|
| Java JDK | `jdk.version` from WorkloadDependencies | Microsoft OpenJDK **only** |
| Android SDK | `androidsdk` from WorkloadDependencies | Use packages array |
| Platform Tools | `androidsdk.packages` | ADB, fastboot |
| Build Tools | `androidsdk.buildToolsVersion` | AAPT2, dx |

## iOS/macOS Development

| Component | Source | Notes |
|-----------|--------|-------|
| Xcode | `xcode.version` from WorkloadDependencies | From iOS workload manifest |
| iOS SDK | `sdk.version` from WorkloadDependencies | Bundled with Xcode |
| iOS Simulator | Any | At least one device |
