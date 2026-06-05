---
name: fsharp-project-organization
description: "Organize F# projects correctly given that F# compilation is order-dependent. Use when creating or restructuring an .fsproj, fixing 'value or constructor is not defined' errors caused by file/declaration order, deciding between namespace and module, adding signature (.fsi) files, or applying RequireQualifiedAccess/AutoOpen. Covers explicit Compile ordering in the .fsproj, top-to-bottom no-forward-reference rule, namespace vs module, signature files, and layering. Do not use for general build/MSBuild tuning (use the dotnet-msbuild skills)."
license: MIT
---

# F# Project Organization

## Purpose

F# resolves names strictly in order - within a file, top to bottom, and across files, in the
order they appear in the `.fsproj`. Getting that order and the namespace/module structure right
prevents a whole class of "not defined" errors and keeps the codebase navigable.

## When to Use

- Creating or restructuring an `.fsproj`
- Diagnosing "The value or constructor 'X' is not defined" caused by ordering
- Choosing between a `namespace` and a `module` at the top of a file
- Introducing signature (`.fsi`) files to lock down a public API
- Applying `[<RequireQualifiedAccess>]` / `[<AutoOpen>]`

## When Not to Use

- General MSBuild/build performance or packaging - use the `dotnet-msbuild` skills

## The order rule

There are no forward references in F#. A name must be **defined above its use**:

- Within a file: declarations are read top to bottom.
- Across files: the order is the order of `<Compile Include=...>` items in the `.fsproj`, not
  alphabetical and not folder order.

```xml
<ItemGroup>
  <Compile Include="Domain/Types.fs" />       <!-- defined first -->
  <Compile Include="Domain/Validation.fs" />  <!-- can use Types -->
  <Compile Include="Workflows/PlaceOrder.fs" />
  <Compile Include="Program.fs" />            <!-- entry point last -->
</ItemGroup>
```

Reorder these items to fix ordering errors; do not try to add forward declarations.

## namespace vs module at the top of a file

| | `namespace` | `module` (top-level) |
|---|------------|----------------------|
| Spans multiple files | yes | no (one file) |
| Can hold functions directly | no (only types, or an inner module) | yes |
| Best for | grouping types across files | a file that is mostly functions |

```fsharp
namespace Fabrikam.Domain

type Customer = { Name: string }

module Customer =                  // inner module for functions over the type
    let rename name c = { c with Name = name }
```

```fsharp
module Fabrikam.Utilities         // a function-heavy file

let add x y = x + y
```

## Signature files (.fsi)

A `.fsi` file sits immediately before its `.fs` file in the `.fsproj` and defines the public
surface; everything not listed is private.

```fsharp
// Customer.fsi
namespace Fabrikam.Domain

type Customer = { Name: string }

module Customer =
    val rename: string -> Customer -> Customer
```

Introduce signature files once an API is stable - they add friction (changes must be made in
both files) but give a clean, enforced public surface.

## RequireQualifiedAccess and AutoOpen

- `[<RequireQualifiedAccess>]` on a module forces callers to qualify (`Order.create`), avoiding
  name collisions and ambiguity from `open` ordering. Use it for modules that shadow or extend
  `FSharp.Core` modules (a custom `Result`/`List`-like module).
- `[<AutoOpen>]` opens a module automatically with its namespace. Use sparingly - it pollutes
  scope. Good for a small operators module or extension members.

## Suggested layering

Modules reference only modules above them. A common bottom-to-top order:

```
Common / primitives  ->  Domain types  ->  Validation  ->  Workflows
  ->  DTOs / serialization  ->  Infrastructure  ->  API  ->  Composition root (Program.fs)
```

## Workflow

1. List types and functions; identify dependencies (who uses whom).
2. Order `.fsproj` `<Compile>` items so every definition precedes its use.
3. Choose `namespace` (types across files) or `module` (function-heavy file) per file.
4. Apply `[<RequireQualifiedAccess>]` where names collide; `[<AutoOpen>]` only where justified.
5. Add `.fsi` files for stable public APIs.
6. Verify with `dotnet build`.

## Validation

- [ ] `.fsproj` `<Compile>` order places every definition before its use
- [ ] No "value or constructor is not defined" errors from ordering
- [ ] Each file's top-level `namespace`/`module` choice fits its contents
- [ ] Collision-prone modules carry `[<RequireQualifiedAccess>]`
- [ ] Signature files (if used) precede their `.fs` files and compile
- [ ] `dotnet build` succeeds

## Common Pitfalls

| Pitfall | Correction |
|---------|------------|
| Assuming alphabetical/folder order compiles | Order is the explicit `.fsproj` `<Compile>` list |
| Trying to forward-reference a later type | Move the definition earlier in file/project order |
| `module` at file top when types should span files | Use a `namespace` with inner modules |
| Overusing `[<AutoOpen>]` | Reserve for small operator/extension modules |
| Adding `.fsi` to a still-churning API | Wait until the API stabilizes |

## More info

- F# project structure / compiler order: https://learn.microsoft.com/en-us/dotnet/fsharp/style-guide/component-design-guidelines
- Signature files: https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/signature-files
