# Microsoft OpenJDK Requirements

.NET MAUI requires **Microsoft Build of OpenJDK** for Android development. Other JDK distributions (Oracle, Azul, Amazon Corretto, etc.) are **not supported**.

> **⚠️ TEMPORARY**: Recommend JDK 21 (not 17) until WorkloadDependencies.json is updated in a future Android workload release. If WorkloadDependencies suggests ≤21, use 21. If it suggests >21, use that value.

## Why Microsoft OpenJDK Only?

- Tested and validated with .NET MAUI toolchain
- Consistent behavior across all platforms
- Long-term support with security updates
- Official recommendation from Microsoft documentation

## Identifying Microsoft OpenJDK

Run `java -version`. Microsoft OpenJDK output contains `Microsoft` in the runtime line:

```
openjdk version "17.0.14" 2025-01-21 LTS
OpenJDK Runtime Environment Microsoft-XXXXXXX (build 17.0.14+7-LTS)
OpenJDK 64-Bit Server VM Microsoft-XXXXXXX (build 17.0.14+7-LTS, mixed mode, sharing)
```

If the output does NOT contain "Microsoft", the wrong JDK is installed or selected.

## Known Installation Paths

These paths are useful for detecting whether Microsoft OpenJDK is already installed.

### macOS

```
/Library/Java/JavaVirtualMachines/microsoft-{VERSION}.jdk/Contents/Home
```

Detection:
```bash
ls -d /Library/Java/JavaVirtualMachines/microsoft-*.jdk 2>/dev/null
/usr/libexec/java_home -V 2>&1 | grep -i microsoft
```

### Windows

```
C:\Program Files\Microsoft\jdk-{VERSION}\
```

Registry: `HKLM\SOFTWARE\Microsoft\JDK\{VERSION}`

Detection:
```powershell
Get-ChildItem "C:\Program Files\Microsoft" -Filter "jdk-*" -ErrorAction SilentlyContinue
java -version 2>&1 | Select-String "Microsoft"
```

### Linux

```
/usr/lib/jvm/msopenjdk-{VERSION}/
```

Detection:
```bash
ls -d /usr/lib/jvm/msopenjdk-* 2>/dev/null
java -version 2>&1 | grep -i "Microsoft"
```

---

## Installation

For installation instructions, refer to the official Microsoft documentation:

- [Microsoft OpenJDK Installation Guide](https://learn.microsoft.com/en-us/java/openjdk/install)
- [Microsoft OpenJDK Download](https://learn.microsoft.com/en-us/java/openjdk/download)

---

## JAVA_HOME Guidance

**JAVA_HOME is NOT required.** .NET MAUI tools auto-detect JDK installations.

| State | OK? | Action |
|-------|-----|--------|
| Not set | ✅ | None needed, auto-detection works |
| Set to Microsoft JDK path | ✅ | None needed |
| Set to non-Microsoft JDK | ❌ | Unset it or point to Microsoft JDK |

To unset:
```bash
# macOS/Linux
unset JAVA_HOME

# Windows PowerShell
Remove-Item Env:JAVA_HOME
```

### Multiple JDKs Installed

1. Run `java -version` and check for "Microsoft" in output
2. If wrong vendor and `JAVA_HOME` is set → unset it or point to Microsoft JDK path
3. If wrong vendor and `JAVA_HOME` is NOT set → the non-Microsoft JDK may be first in PATH; install Microsoft JDK and it should take precedence
4. Restart terminal after changes

---

## Official Resources

- [Microsoft OpenJDK Installation Guide](https://learn.microsoft.com/en-us/java/openjdk/install)
- [Microsoft OpenJDK Download](https://learn.microsoft.com/en-us/java/openjdk/download)
- [Microsoft OpenJDK GitHub](https://github.com/microsoft/openjdk)
