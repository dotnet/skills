# Linux Platform Requirements

⚠️ **Linux has limited support** - Android targets only.

## Required Components

| Component | Requirement | Notes |
|-----------|-------------|-------|
| .NET SDK | Active support | Query releases-index.json |
| Java JDK | Per WorkloadDependencies | Microsoft OpenJDK **only** |
| Android SDK | Per WorkloadDependencies | Use packages array |

## Required Workloads

| Workload | Required | Purpose |
|----------|----------|---------|
| `maui-android` | ✅ Yes | MAUI for Android |
| `android` | ✅ Yes | Android targets |

**Important**: Use `maui-android` NOT `maui` on Linux. The `maui` workload is a meta-workload that includes iOS/Mac dependencies which won't install on Linux.

## Limitations

- ❌ No iOS support (requires macOS)
- ❌ No Mac Catalyst support (requires macOS)
- ❌ No Windows support (requires Windows)

## Android Development

| Component | Source | Notes |
|-----------|--------|-------|
| Java JDK | `jdk.version` from WorkloadDependencies | Microsoft OpenJDK **only** |
| Android SDK | `androidsdk` from WorkloadDependencies | Use packages array |
| Platform Tools | `androidsdk.packages` | ADB, fastboot |
| Build Tools | `androidsdk.buildToolsVersion` | AAPT2, dx |
| KVM | Enabled | For emulator acceleration |
