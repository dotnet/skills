# Windows Installation Commands

## Windows SDK

The Windows SDK is required for WinUI 3 / Windows targets.

### Detect Windows SDK

```powershell
Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots" -ErrorAction SilentlyContinue
```

### Install Windows SDK

The Windows SDK is typically installed as part of the .NET MAUI workload or via the Visual Studio Installer.

For standalone installation, see: https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/
