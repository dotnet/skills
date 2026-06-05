---
name: convert-csharp-to-fsharp
description: "Translate C# code into idiomatic F#, not a literal line-by-line port. Use when porting a C# class, method, or file to F#, or when an F# translation needs to be made idiomatic. Produces F# that uses records/DUs, Option over null, Result over exceptions, pattern matching, and pipelines. Composes the writing-idiomatic-fsharp, fsharp-domain-modeling, and fsharp-error-handling skills, plus design-fsharp-for-dotnet-interop when the result must stay C#-consumable. Do not use for fresh F# design with no C# source (use writing-idiomatic-fsharp / fsharp-domain-modeling)."
license: MIT
---

# Converting C# to F#

## Purpose

Port C# to F# that an F# developer would actually write - leveraging records, discriminated
unions, `Option`, `Result`, pattern matching, and pipelines - rather than transliterating C#
syntax into `.fs`.

## When to Use

- Porting a C# class, method, or file to F#
- Cleaning up an F# translation that still reads like C#
- Migrating a component to F# while keeping (or dropping) C# consumability

## When Not to Use

- Designing fresh F# with no C# source - use `writing-idiomatic-fsharp` and
  `fsharp-domain-modeling` directly

## How C# constructs map to idiomatic F#

| C# | Idiomatic F# |
|----|--------------|
| POCO / DTO class with get/set | `record` (immutable; `with` for updates) |
| `enum` | discriminated union (or enum if interop needs it) |
| class hierarchy / `abstract` + subclasses for variants | discriminated union + `match` |
| `null` / nullable reference | `Option` (`Some`/`None`) |
| `throw` / `try-catch` for expected failures | `Result` + `result { }` (see `fsharp-error-handling`) |
| `if/else if` chains, `switch` | `match` |
| `for`/`foreach` building a collection | `List`/`Seq` `map`/`filter`/`fold` |
| `interface` with methods | `interface` (kept) or a record/DU of functions where simpler |
| `static` helper class | a `module` of functions |
| LINQ (`Where`/`Select`/`Aggregate`) | `List.filter`/`List.map`/`List.fold` pipelines |
| `async`/`await`, `Task<T>` | `task { }` / `async { }` (see `fsharp-async-and-tasks`) |

## Workflow

1. **Understand the C#** - identify data types, control flow, error handling, and async.
2. **Model the data first** (`fsharp-domain-modeling`): DTOs to records, enums/variants to DUs,
   nullable to `Option`; add smart constructors for validated values.
3. **Translate behavior** (`writing-idiomatic-fsharp`): `switch`/`if` to `match`, loops to
   collection functions, nested calls to pipelines, LINQ to `List`/`Seq` functions.
4. **Convert error handling** (`fsharp-error-handling`): expected exceptions to `Result`; wrap
   genuinely throwing .NET calls at the boundary.
5. **Convert async** (`fsharp-async-and-tasks`): `Task`/`await` to `task { }`/`async { }`; no
   `.Result` blocking.
6. **Preserve interop if required** (`design-fsharp-for-dotnet-interop`): if C# must still
   consume the result, keep the public surface C#-friendly.
7. **Mind file order**: in a project, place definitions before use in `.fsproj`
   (`fsharp-project-organization`).
8. **Verify**: build/run and, where practical, port or run the existing tests to confirm
   behavior is preserved.

## Validation

- [ ] DTO classes became immutable records; variant hierarchies/enums became DUs
- [ ] `null` replaced with `Option`; expected exceptions replaced with `Result`
- [ ] `switch`/`if` chains became `match`; loops became collection functions; LINQ became
      pipelines
- [ ] `async`/`await` became `task`/`async` with no blocking `.Result`
- [ ] Behavior preserved (tests pass or output matches the C# original)
- [ ] If C#-consumable: public surface still follows the interop rules

## Common Pitfalls

| Pitfall | Correction |
|---------|------------|
| Line-by-line transliteration | Re-model the data and control flow idiomatically first |
| Keeping `null` and `try/catch` as-is | Map to `Option`/`Result` |
| Porting mutable classes verbatim | Prefer records/DUs; isolate mutation if genuinely needed |
| Ignoring `.fsproj` file order | Order definitions before use; entry point last |
| Breaking C# callers during a partial migration | Apply `design-fsharp-for-dotnet-interop` to the public surface |

## More info

- F# style guide: https://learn.microsoft.com/en-us/dotnet/fsharp/style-guide/
- F# for C#/OO developers: https://learn.microsoft.com/en-us/dotnet/fsharp/
