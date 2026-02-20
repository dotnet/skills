# Incremental Build: Inputs and Outputs on Custom Targets

Custom targets **must** specify `Inputs` and `Outputs` attributes so MSBuild can skip them when up-to-date. Without both attributes, the target runs on every build.

```xml
<!-- BAD: Runs every time -->
<Target Name="GenerateBuildInfo" BeforeTargets="CoreCompile">
  <WriteLinesToFile File="$(IntermediateOutputPath)BuildInfo.g.cs"
                    Lines="// Generated at $(Version)" Overwrite="true" />
</Target>

<!-- GOOD: Skipped when up-to-date -->
<Target Name="GenerateBuildInfo" BeforeTargets="CoreCompile"
        Inputs="$(MSBuildProjectFile)" Outputs="$(IntermediateOutputPath)BuildInfo.g.cs">
  <WriteLinesToFile File="$(IntermediateOutputPath)BuildInfo.g.cs"
                    Lines="// Generated at $(Version)" Overwrite="true" />
  <ItemGroup>
    <FileWrites Include="$(IntermediateOutputPath)BuildInfo.g.cs" />
    <Compile Include="$(IntermediateOutputPath)BuildInfo.g.cs" />
  </ItemGroup>
</Target>
```

**Key points:**
- **`Inputs`** should include `$(MSBuildProjectFile)` plus any source files that drive generation
- **`Outputs`** should use `$(IntermediateOutputPath)` so generated files go in `obj/` and are managed by MSBuild
- **`FileWrites`** registration ensures `dotnet clean` removes the generated file
- **`Compile` inclusion** adds the generated file to compilation without requiring it at evaluation time

See the `incremental-build` skill for deep guidance on diagnosing broken incremental builds, FileWrites tracking, and Visual Studio's Fast Up-to-Date Check.
