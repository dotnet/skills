# Windows Platform Requirements

## Required Components

| Component | Requirement | Notes |
|-----------|-------------|-------|
| Windows | 10 or later | 64-bit required |
| .NET SDK | Active support | Query releases-index.json for latest |
| Windows App SDK | Current | Required for WinUI 3 / Windows targets |

## Required Workloads

| Workload | Required | Purpose |
|----------|----------|---------|
| `maui` or `maui-windows` | ✅ Yes | Core MAUI framework |
| `android` | ✅ Yes | Android targets |
| `ios` | Optional | iOS targets (requires Mac build host) |
| `maccatalyst` | Optional | Mac Catalyst (requires Mac build host) |

## Android Development

| Component | Source | Notes |
|-----------|--------|-------|
| Java JDK | `jdk.version` from WorkloadDependencies | Microsoft OpenJDK **only** |
| Android SDK | `androidsdk` from WorkloadDependencies | Use packages array |
| Android Emulator | Latest | With HAXM or Hyper-V |
| Platform Tools | `androidsdk.packages` | ADB, fastboot |
| Build Tools | `androidsdk.buildToolsVersion` | AAPT2, dx |

## Windows App Development

| Component | Requirement | Notes |
|-----------|-------------|-------|
| Windows App SDK | Current | Required for WinUI 3 |
| Windows SDK | Recent | Windows 10+ SDK |
