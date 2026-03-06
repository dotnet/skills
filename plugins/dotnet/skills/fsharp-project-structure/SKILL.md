---
name: fsharp-project-structure
description: "F# .fsproj file ordering, adding .fs/.fsi files, fixing FS0039 and FS0034 compilation order errors, signature files."
---

# F# Project File Structure

F# compiles `<Compile Include>` items sequentially, top to bottom. A file can only reference types/modules from files listed **above** it in the .fsproj.

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| .fsproj file | Yes | The F# project file to modify |

## File compilation order

- File B can use types from file A only if A is listed BEFORE B in the .fsproj
- Entry point (`Program.fs` or `[<EntryPoint>]`) must be LAST
- When adding a file: check its `open` declarations, insert AFTER the last dependency but BEFORE any consumer
- Wrong order symptom: `FS0039 "The type/value/namespace 'X' is not defined"` where X exists in another project file

Example ordering:
```xml
<ItemGroup>
  <Compile Include="Domain.fs" />
  <Compile Include="Services.fs" />
  <Compile Include="Program.fs" />
</ItemGroup>
```

## Signature files (.fsi)

- A `.fsi` defines the public API contract for its companion `.fs` file
- Contains type signatures and `val` declarations — no implementation
- `.fsi` MUST appear immediately BEFORE its `.fs` in the Compile list
- If `.fsi` exists, public members in `.fs` not declared in `.fsi` become internal
- `FS0034` (ValueNotContained): signature and implementation don't match

Example with signatures:
```xml
<ItemGroup>
  <Compile Include="Domain.fsi" />
  <Compile Include="Domain.fs" />
  <Compile Include="Services.fsi" />
  <Compile Include="Services.fs" />
  <Compile Include="Program.fs" />
</ItemGroup>
```

## Workflow

1. Identify where the new file fits in the dependency chain
2. Add `<Compile Include>` at the correct position in .fsproj
3. Run `dotnet build` to verify

## Pitfalls

| Pitfall | Fix |
|---------|-----|
| New file appended after Program.fs | Insert before Program.fs, after its dependencies |
| `.fsi` placed AFTER its `.fs` | Must be immediately BEFORE |
| Deleting `.fsi` to "fix" FS0034 | Update the `.fsi` to match the new API instead |
| Circular dependency between files | Split one file into two |
| Modifying source to work around order | Reorder `<Compile>` items in .fsproj instead |
