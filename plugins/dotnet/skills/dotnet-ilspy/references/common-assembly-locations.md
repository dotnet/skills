# Common .NET Assembly Locations for ILSpy

Use this reference when the user knows the package, framework, or app name but not the exact assembly path to decompile.

## NuGet packages

Restore packages first if needed, then inspect the package cache.

Typical locations:

```text
~/.nuget/packages/<package-name>/<version>/lib/<tfm>/
~/.nuget/packages/<package-name>/<version>/runtimes/<rid>/lib/<tfm>/
~/.nuget/packages/<package-name>/<version>/ref/<tfm>/
```

Guidance:

- Prefer `lib/` for package implementation
- Prefer `runtimes/<rid>/lib/` when the package ships runtime-specific binaries
- Use `ref/` only to inspect public API shape; it is usually the wrong place for implementation details

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
```

Examples:

```text
C:/Program Files/dotnet/shared/Microsoft.NETCore.App/8.0.0/System.Text.Json.dll
C:/Program Files/dotnet/shared/Microsoft.AspNetCore.App/8.0.0/Microsoft.AspNetCore.Server.Kestrel.Core.dll
```

## SDK reference packs

Reference packs are useful for API shape, not runtime implementation.

Typical location:

```text
C:/Program Files/dotnet/packs/Microsoft.NETCore.App.Ref/<version>/ref/net8.0/
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

```bash
dotnet --list-runtimes
dnx ilspycmd -l class "path/to/Assembly.dll"
dnx ilspycmd -t Namespace.TypeName "path/to/Assembly.dll"
dnx ilspycmd -il -t Namespace.TypeName "path/to/Assembly.dll"
```

If `dnx ilspycmd` is unavailable, substitute `ilspycmd`.
