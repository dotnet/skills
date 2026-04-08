# .ips Crash Log Format — Additional Reference

Supplementary details for `.ips` crash log parsing and macOS symbol acquisition. For the main workflow, see [SKILL.md](../SKILL.md).

## macOS Symbol Packages

On macOS (`osx-arm64`, `osx-x64`), .NET runtime symbols are distributed differently than iOS:

- **iOS / Mac Catalyst / tvOS**: dSYM bundles ship inside the `Microsoft.NETCore.App.Runtime.<rid>` NuGet package.
- **macOS**: The main runtime package contains binaries but **not** debug symbols.

### Automatic download (built into the script)

The `Symbolicate-Crash.ps1` script automatically downloads `.dwarf` files from the Microsoft symbol server using the Mach-O UUID:

```
URL pattern: https://msdl.microsoft.com/download/symbols/_.dwarf/mach-uuid-sym-{UUID}/_.dwarf
```

The UUID is extracted from the crash log's binary image list and normalized by lowercasing and removing dashes before constructing the symbol server URL. The server returns HTTP 302 on hit, 404 on miss. Downloaded files are cached in `$TMPDIR/dotnet-crash-symbols/` and converted to `.dSYM` bundles automatically.

### Manual fallback: `.symbols` NuGet package

Download the separate **`Microsoft.NETCore.App.Runtime.<rid>.symbols`** package (note `.symbols` suffix — not `.snupkg`):

```bash
curl -Lo symbols.nupkg https://api.nuget.org/v3-flatcontainer/microsoft.netcore.app.runtime.osx-arm64.symbols/10.0.4/microsoft.netcore.app.runtime.osx-arm64.symbols.10.0.4.nupkg
unzip -q symbols.nupkg -d symbols-extracted
```

## Supported RIDs

`ios-arm64`, `iossimulator-arm64`, `iossimulator-x64`, `tvos-arm64`, `tvossimulator-arm64`, `tvossimulator-x64`, `maccatalyst-arm64`, `maccatalyst-x64`, `osx-arm64`, `osx-x64`.
