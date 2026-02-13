# Project Setup and CI Validation for AOT

MSBuild properties, analyzer configuration, warning codes, and CI pipeline patterns for Native AOT.

## Contents
- [Essential MSBuild Properties](#essential-msbuild-properties) — Application and library project config
- [Warning Codes Reference](#warning-codes-reference) — IL2xxx trimming, IL3xxx AOT, single-file
- [Roslyn Analyzers vs ILC](#roslyn-analyzers-vs-ilc-full-publish) — When to use each
- [CI Pipeline Validation](#ci-pipeline-validation) — AOT test app pattern, GitHub Actions
- [Multi-Targeting for AOT Compatibility](#multi-targeting-for-aot-compatibility) — Cross-framework annotations
- [Recommended Project Setup Checklist](#recommended-project-setup-checklist)

## Essential MSBuild Properties

### Application Projects

```xml
<PropertyGroup>
  <!-- Enable Native AOT publishing -->
  <PublishAot>true</PublishAot>

  <!-- Show all individual warnings instead of one per assembly -->
  <TrimmerSingleWarn>false</TrimmerSingleWarn>

  <!-- Treat ILC warnings as errors for zero-warning builds -->
  <IlcTreatWarningsAsErrors>true</IlcTreatWarningsAsErrors>
</PropertyGroup>
```

### Library Projects

```xml
<PropertyGroup>
  <!-- Mark the library as AOT-compatible (enables all analyzers) -->
  <IsAotCompatible Condition="$([MSBuild]::IsTargetFrameworkCompatible('$(TargetFramework)', 'net8.0'))">true</IsAotCompatible>

  <!-- Optionally verify all referenced assemblies are also AOT-compatible -->
  <VerifyReferenceAotCompatibility>true</VerifyReferenceAotCompatibility>
</PropertyGroup>
```

Setting `IsAotCompatible` to `true` automatically enables:
- `IsTrimmable` — marks the assembly as safe to trim
- `EnableTrimAnalyzer` — Roslyn analyzer for trim warnings
- `EnableSingleFileAnalyzer` — Roslyn analyzer for single-file warnings
- `EnableAotAnalyzer` — Roslyn analyzer for AOT warnings

### Optimization Properties

```xml
<PropertyGroup>
  <!-- Optimize for size vs speed -->
  <OptimizationPreference>Size</OptimizationPreference> <!-- or Speed -->

  <!-- Reduce binary size by using invariant globalization -->
  <InvariantGlobalization>true</InvariantGlobalization>

  <!-- Enable EventPipe support for diagnostics -->
  <EventSourceSupport>true</EventSourceSupport>

  <!-- Keep debug symbols in a separate file (default) -->
  <StripSymbols>true</StripSymbols>

  <!-- Enable source generators for configuration binding -->
  <EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>
</PropertyGroup>
```

## Warning Codes Reference

### Trimming Warnings (IL2xxx)

| Code | Description | Common Fix |
|------|-------------|------------|
| IL2026 | Using member with `[RequiresUnreferencedCode]` | Eliminate reflection or propagate `[RequiresUnreferencedCode]` |
| IL2046 | `[RequiresUnreferencedCode]` mismatch on override/implementation | Add matching attribute to override |
| IL2055 | Call to `Type.MakeGenericType` with unknown type | Use static dispatch or root needed instantiations |
| IL2057 | Unrecognized value passed to `Type.GetType` | Use compile-time known type names or `typeof()` |
| IL2067 | Parameter doesn't satisfy `[DynamicallyAccessedMembers]` in target | Add `[DynamicallyAccessedMembers]` to parameter |
| IL2070 | `this` argument doesn't satisfy `[DynamicallyAccessedMembers]` | Annotate the `Type` source with required members |
| IL2072 | Return value doesn't satisfy `[DynamicallyAccessedMembers]` | Annotate return value or method |
| IL2075 | `Type.GetType` return value used in reflection | Use `typeof()` instead of `Type.GetType(string)` |
| IL2077 | Field doesn't satisfy `[DynamicallyAccessedMembers]` in target | Add `[DynamicallyAccessedMembers]` to the field |
| IL2104 | Assembly produced trim warnings | Fix warnings inside the assembly or contact library author |
| IL2125 | Referenced assembly not annotated as trim-compatible | Verify the dependency is trim-safe or contact author |

### AOT Warnings (IL3xxx)

| Code | Description | Common Fix |
|------|-------------|------------|
| IL3050 | Using member with `[RequiresDynamicCode]` | Eliminate dynamic code or use `RuntimeFeature.IsDynamicCodeSupported` guard |
| IL3051 | `[RequiresDynamicCode]` mismatch on override | Add matching attribute to override |
| IL3052 | COM marshalling type not supported in AOT | Use `ComWrappers` API instead |
| IL3053 | COM interop not supported in AOT | Use `ComWrappers` API instead |
| IL3058 | Referenced assembly not annotated as AOT-compatible | Check dependency status or contact library author |

### Single File Warnings (IL3xxx)

| Code | Description | Common Fix |
|------|-------------|------------|
| IL3000 | `Assembly.Location` returns empty string in single-file | Use `AppContext.BaseDirectory` instead |
| IL3001 | `Assembly.GetFile` not supported in single-file | Use embedded resources or `AppContext.BaseDirectory` |
| IL3002 | Using member with `[RequiresAssemblyFiles]` | Avoid assembly file access or guard with `!IsPublishedAsSingleFile` |

## Roslyn Analyzers vs ILC (Full Publish)

| Capability | Roslyn Analyzers | ILC (dotnet publish) |
|-----------|:---:|:---:|
| IDE integration (squiggles) | ✅ | 🔴 |
| Immediate feedback | ✅ | 🔴 |
| Whole-program analysis | 🔴 | ✅ |
| Analyzes dependencies | 🔴 | ✅ |
| Guaranteed complete warning set | 🔴 | ✅ |
| Requires publish step | 🔴 | ✅ |

**Recommendation**: Use both. Roslyn analyzers for fast feedback during development. Full publish for CI validation.

## CI Pipeline Validation

### AOT Compatibility Test App Pattern

Create a dedicated test project that exercises your library APIs and publishes with AOT:

**1. Create the test project**
```xml
<!-- test/AotCompatibility.TestApp/AotCompatibility.TestApp.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <TrimmerSingleWarn>false</TrimmerSingleWarn>
    <IlcTreatWarningsAsErrors>true</IlcTreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/MyLibrary/MyLibrary.csproj" />
    <TrimmerRootAssembly Include="MyLibrary" />
  </ItemGroup>
</Project>
```

`TrimmerRootAssembly` ensures every method in your library is analyzed, even if not called by the test app.

**2. Create a publish-and-verify script**
```bash
#!/bin/bash
set -e
dotnet publish test/AotCompatibility.TestApp/ \
  -c Release \
  -r linux-x64 \
  --no-restore \
  2>&1 | tee publish-output.txt

# Check for warnings (IlcTreatWarningsAsErrors will fail the build if any exist)
echo "AOT publish succeeded with zero warnings"
```

**3. Add to CI workflow**
```yaml
# .github/workflows/aot-compat.yml
name: AOT Compatibility
on:
  pull_request:
    paths: ['src/**', 'test/AotCompatibility.TestApp/**']
jobs:
  aot-check:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: sudo apt-get install -y clang zlib1g-dev
      - run: dotnet restore
      - run: dotnet publish test/AotCompatibility.TestApp/ -c Release -r linux-x64
```

### Running the Published AOT Binary

For libraries with suppressed warnings, also execute the published binary to verify runtime behavior:

```bash
# After publish
./test/AotCompatibility.TestApp/bin/Release/net10.0/linux-x64/publish/AotCompatibility.TestApp
echo "Exit code: $?"
```

## Multi-Targeting for AOT Compatibility

### Library Targeting Multiple Frameworks

```xml
<PropertyGroup>
  <TargetFrameworks>netstandard2.0;net8.0;net10.0</TargetFrameworks>
  <IsAotCompatible
    Condition="$([MSBuild]::IsTargetFrameworkCompatible('$(TargetFramework)', 'net8.0'))">true</IsAotCompatible>
</PropertyGroup>
```

### Using Annotations Across Frameworks

If your library targets frameworks before `net7.0` where trim/AOT attributes don't exist, you have two options:

**Option 1: `#if` directives**
```csharp
public static object CreateInstance(
#if NET7_0_OR_GREATER
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
#endif
    Type type)
{
    return Activator.CreateInstance(type);
}
```

**Option 2: Define the attributes internally**

Copy attribute definitions into your project — the trim/AOT tools recognize them by name and namespace regardless of which assembly defines them. This ensures annotations are present on all target frameworks.

You can also use the [PolySharp](https://www.nuget.org/packages/PolySharp/) NuGet package to auto-generate polyfill attribute definitions at build time.

## Recommended Project Setup Checklist

For applications:
- [ ] `<PublishAot>true</PublishAot>` in project file (not just on command line)
- [ ] `<TrimmerSingleWarn>false</TrimmerSingleWarn>` to see all warnings
- [ ] `<IlcTreatWarningsAsErrors>true</IlcTreatWarningsAsErrors>` for zero-warning builds
- [ ] `<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>` if using `IConfiguration`
- [ ] `JsonSerializerContext` registered for all serialized types
- [ ] `[LoggerMessage]` used for all logging
- [ ] `[GeneratedRegex]` used for all regex patterns
- [ ] `dotnet publish -r <RID>` run and verified zero warnings
- [ ] Published binary executed and tested

For libraries:
- [ ] `<IsAotCompatible>true</IsAotCompatible>` set (with TFM condition if multi-targeting)
- [ ] AOT compatibility test app created with `TrimmerRootAssembly`
- [ ] CI pipeline runs AOT publish on every PR
- [ ] All public APIs either annotated or AOT-compatible
- [ ] No `#pragma warning disable` used for trim/AOT warnings (use `[UnconditionalSuppressMessage]`)
