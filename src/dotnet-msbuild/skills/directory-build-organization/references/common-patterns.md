# Common Directory.Build Patterns

## Conditional Settings by Project Type

Detect test projects by naming convention in `Directory.Build.props`:

```xml
<PropertyGroup Condition="$(MSBuildProjectName.EndsWith('.Tests')) OR $(MSBuildProjectName.EndsWith('.UnitTests'))">
  <IsPackable>false</IsPackable>
  <IsTestProject>true</IsTestProject>
</PropertyGroup>
```

Use `Directory.Build.targets` for conditions on SDK-defined properties like `OutputType`:

```xml
<PropertyGroup Condition="'$(OutputType)' == 'Exe'">
  <SelfContained>false</SelfContained>
</PropertyGroup>

<PropertyGroup Condition="'$(OutputType)' == 'Library' AND '$(IsTestProject)' != 'true'">
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
</PropertyGroup>
```

## Post-Build Validation

Validate that `Pack` produced the expected output:

```xml
<Target Name="ValidatePackageOutput" AfterTargets="Pack"
        Condition="'$(IsPackable)' == 'true'">
  <Error Text="Package was not created at $(PackageOutputPath)$(PackageId).$(PackageVersion).nupkg"
         Condition="!Exists('$(PackageOutputPath)$(PackageId).$(PackageVersion).nupkg')" />
</Target>
```

## Artifact Output Layout (.NET 8+)

Setting `ArtifactsPath` in `Directory.Build.props` produces this structure:

```
artifacts/
  bin/
    MyLib/
      debug/
      release/
    MyApp/
      debug/
      release/
  obj/
    MyLib/
    MyApp/
  publish/
    MyApp/
```
