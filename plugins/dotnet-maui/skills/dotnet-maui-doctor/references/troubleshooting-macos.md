# macOS Troubleshooting

## Xcode Issues

### "xcode-select: error: no developer tools found"

**Solution**:
```bash
xcode-select --install
```

### "Xcode not found at expected location"

**Solution**:
```bash
# List Xcode installations
ls -d /Applications/Xcode*.app 2>/dev/null

# Set active Xcode
sudo xcode-select -s /Applications/Xcode.app/Contents/Developer
```

### "Unable to boot simulator"

**Causes & Solutions**:

1. **No simulators installed**:
   ```bash
   xcrun simctl list devices available
   xcrun simctl create "iPhone 16" "com.apple.CoreSimulator.SimDeviceType.iPhone-16"
   ```

2. **Simulator runtime not installed**:
   - Download an iOS runtime via `xcodebuild -downloadPlatform iOS` or from Xcode → Settings → Platforms

3. **Corrupted simulator**:
   ```bash
   xcrun simctl erase all
   ```

### "Code signing error"

**Cause**: Missing or invalid provisioning profile.

**Solution**:
1. Open Xcode → Settings → Accounts
2. Add/refresh Apple Developer account
3. Download provisioning profiles

---

## macOS Performance

### Slow iOS simulator

1. Close other resource-intensive apps
2. Use recent simulator device (not legacy)
3. Reduce debugger verbosity
4. Use physical device for performance testing

---

## macOS Diagnostic Commands

```bash
# Xcode info
xcodebuild -version
xcode-select -p

# JDK detection (macOS-specific)
/usr/libexec/java_home -V

# Android SDK location
echo $ANDROID_SDK_ROOT
# Default: ~/Library/Android/sdk

# Logs
# ~/Library/Logs/Xamarin/
```
