# Windows Troubleshooting

## Emulator Issues

### Hyper-V conflict with Android Emulator

**Cause**: HAXM and Hyper-V cannot coexist.

**Solution**:
- Use Android Emulator Hypervisor Driver instead of HAXM
- Or disable Hyper-V: `bcdedit /set hypervisorlaunchtype off`

---

## Windows Diagnostic Commands

```powershell
# Windows SDK detection
Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots" -ErrorAction SilentlyContinue

# JDK detection (Windows-specific)
Get-ChildItem "C:\Program Files\Microsoft" -Filter "jdk-*" -ErrorAction SilentlyContinue
java -version 2>&1 | Select-String "Microsoft"

# Android SDK location
echo $env:ANDROID_SDK_ROOT
# Default: $env:LOCALAPPDATA\Android\Sdk

# Android SDK list installed (Windows)
& "$env:ANDROID_SDK_ROOT\cmdline-tools\latest\bin\sdkmanager.bat" --list_installed

# Logs
# %LOCALAPPDATA%\Xamarin\Logs\
```
