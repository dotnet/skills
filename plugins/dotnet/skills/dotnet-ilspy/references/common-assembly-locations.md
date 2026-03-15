# Common .NET Assembly Locations for ILSpy

Use this reference when the user knows the package, framework, or app name but not the exact assembly path to decompile.

In the paths below, `tfm` means target framework moniker such as `net8.0`, and `rid` means runtime identifier such as `win-x64`.

## NuGet packages

Restore packages first if needed, then inspect the package cache.

Typical locations:

```text
~/.nuget/packages/<package-name>/<version>/lib/<tfm>/
~/.nuget/packages/<package-name>/<version>/runtimes/<rid>/lib/<tfm>/
~/.nuget/packages/<package-name>/<version>/ref/<tfm>/
C:/Users/<user>/.nuget/packages/<package-name>/<version>/lib/<tfm>/
```

Guidance:

- Prefer `lib/` for package implementation
- Prefer `runtimes/<rid>/lib/` when the package ships runtime-specific binaries
- Use `ref/` only to inspect public API shape; it is usually the wrong place for implementation details
- Match both the package version and target framework to the user's actual app before drawing conclusions

Example:

```text
~/.nuget/packages/newtonsoft.json/13.0.3/lib/netstandard2.0/Newtonsoft.Json.dll
```

## Shared framework assemblies

Use `dotnet --list-runtimes` to find the installed shared frameworks and versions.

Typical locations:

```text
C:/Program Files/dotnet/shared/Microsoft.NETCore.App/<version>/
C:/Program Files/dotnet/shared/Microsoft.AspNetCore.App/<version>/
/usr/share/dotnet/shared/Microsoft.NETCore.App/<version>/
/usr/share/dotnet/shared/Microsoft.AspNetCore.App/<version>/
/usr/local/share/dotnet/shared/Microsoft.NETCore.App/<version>/
/usr/local/share/dotnet/shared/Microsoft.AspNetCore.App/<version>/
```

Examples:

```text
C:/Program Files/dotnet/shared/Microsoft.NETCore.App/10.0.2/System.Text.Json.dll
C:/Program Files/dotnet/shared/Microsoft.AspNetCore.App/10.0.2/Microsoft.AspNetCore.Server.Kestrel.Core.dll
```

## SDK reference packs

Reference packs are useful for API shape, not runtime implementation.

Typical location:

```text
C:/Program Files/dotnet/packs/Microsoft.NETCore.App.Ref/<version>/ref/<tfm>/
```

If the user asks how something works internally, move from the reference pack to the matching shared runtime assembly.

## Project outputs

Inspect the assembly that the project actually built or published.

Typical locations:

```text
./bin/Debug/<tfm>/
./bin/Release/<tfm>/
./bin/Release/<tfm>/publish/
```

Prefer the publish output when trimming, single-file publishing, or packaging changes the final assembly layout.

## Quick command patterns

```text
dotnet --list-runtimes
dnx ilspycmd -l class "path/to/Assembly.dll"
dnx ilspycmd -t Namespace.TypeName "path/to/Assembly.dll"
dnx ilspycmd -il -t Namespace.TypeName "path/to/Assembly.dll"
```

If `dnx ilspycmd` is unavailable, substitute `ilspycmd`.
