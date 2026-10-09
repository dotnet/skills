# JDK selection and Microsoft OpenJDK

[.NET for Android guidance](https://learn.microsoft.com/dotnet/android/getting-started/installation/dependencies)
recommends Microsoft OpenJDK, tested against .NET for Android builds. It does not
say every other distribution necessarily fails. Nor does a matching major
version prove every distribution works with every workload.

Use the selected workload's `jdk.version` range and `recommendedVersion`, or its
version-specific official requirements if dependency metadata is absent.
Do not replace an existing working JDK merely because its vendor differs.

## Diagnose the executable the build uses

1. Read the exact error and project/CLI/IDE `JavaSdkDirectory` overrides.
2. Inspect that directory's `bin/java` **and** `bin/javac`; a JRE is not a JDK.
   Check version, architecture, existence, permissions and paths containing spaces.
3. Compare with `JAVA_HOME` and the executable resolved on PATH. Shell
   `java -version` may describe a different installation from the build.
4. A supplied log selecting an obsolete JDK outweighs a shell showing a newer one.
   Prefer a project/build-scoped explicit path for a test over global changes.

The log establishes the selected path, not whether it came from a project,
environment, IDE or auto-detection. Do not infer a universal discovery order,
invent an IDE preference page, or dismiss `JAVA_HOME` for every configuration.

For an authorized build, a concrete process-scoped override is:

```bash
dotnet build "$PROJECT" -f "$ANDROID_TFM" "-p:JavaSdkDirectory=$JDK"
```

Use the existing project/TFM and a verified compatible JDK. This is a proposed
build, not read-only inspection; it writes outputs and may restore packages.
Changing a persistent project/IDE setting also requires authorization.

Read-only examples (replace paths with the actual selected directory):

```bash
command -v java
printf '%s\n' "$JAVA_HOME"
"/absolute/jdk/bin/java" -version
"/absolute/jdk/bin/javac" -version
# macOS inventory, only if selection is unclear:
/usr/libexec/java_home -V
```

```powershell
Get-Command java -ErrorAction SilentlyContinue
$env:JAVA_HOME
& 'C:\absolute\jdk\bin\java.exe' -version
& 'C:\absolute\jdk\bin\javac.exe' -version
```

## JAVA_HOME

`JAVA_HOME` is useful for command-line Java tooling and is recommended in the
official manual-install guidance. Some .NET/IDE configurations auto-detect a JDK;
neither auto-detection nor `JAVA_HOME` always establishes the build's selected
directory. An unset variable alone is not a failure. A valid explicit variable
alone is not a reason to unset it. Diagnose actual resolution before changing
shell profiles, PATH, system alternatives, or uninstalling other JDKs.

If installation is needed and approved, recommend a compatible Microsoft Build
of OpenJDK from the [official downloads](https://learn.microsoft.com/java/openjdk/download)
or [installation guide](https://learn.microsoft.com/java/openjdk/install).
Validate selection again afterwards; installing a JDK does not prove the build
uses it.
