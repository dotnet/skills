---
name: writing-idiomatic-fsharp
description: "Write or refactor F# in idiomatic functional style instead of transliterated C#. Use when authoring new F#, refactoring F# that reads like C# (mutable variables, for/while loops, null, if/else statements, classes, .Result/.Wait()), or reviewing F# for idiom. Covers expressions over statements, immutability, |> and >> pipelines, pattern matching, collection functions over loops, Option over null, partial application, and exhaustive matching. Do not use for deliberate hot-path mutation, or for shaping an F# public API for C# consumers (use design-fsharp-for-dotnet-interop)."
license: MIT
---

# Writing Idiomatic F#

## Purpose

Produce F# that an experienced F# developer would write: expression-oriented, immutable by
default, composed with pipelines, and driven by pattern matching - not C# control flow ported
into `.fs` files.

## When to Use

- Authoring new F# code where idiom and readability matter
- Refactoring F# that "reads like C#": mutable accumulators, `for`/`while` loops, `null`,
  statement-style `if/else`, classes where a record or discriminated union fits
- Reviewing F# for non-idiomatic patterns before merge

## When Not to Use

- Deliberate performance-critical mutation (tight loops, pooled buffers); idiom yields to
  measured performance there
- Shaping an F# public API for consumption by C# or other .NET languages - use
  `design-fsharp-for-dotnet-interop`
- Pure formatting concerns - use `format-fsharp-with-fantomas`

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| F# code or intent | Yes | Code to refactor, or a description of what to write |
| Idiom scope | No | Optional focus, e.g. "just the loops" or "just the error flow" |

## Workflow

### Step 1: Identify the C#-isms

Scan for these tells - the most common ways C# habits leak into F#:

- `mutable` bindings and reassignment used as accumulators
- `for` / `while` loops iterating a collection to build a result
- `null` literals and null checks on F# types
- `if/else` used as a statement to pick a value
- `class` with mutable fields where a `record` or discriminated union fits
- `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` on tasks
- Nested `if` / type-tests that should be a single `match`
- `match` with a stray `_` over a closed set of cases

### Step 2: Apply the idiom rewrites

Rewrite one concern at a time. See `references/csharpism-rewrites.md` for the full before/after
catalog. The core moves:

- Statements to expressions: `if cond then a else b` and `match` are values, not control flow
- Mutation to immutable bindings, recursion, or `List.fold`
- Loops to `List`/`Seq`/`Array` functions: `map`, `filter`, `choose`, `fold`, `sumBy`
- `null` to `Option` (`Some`/`None`), composed with `Option.map` / `Option.defaultValue`
- Nested calls to `|>` pipelines; use `>>` composition only where it stays readable
- Flags and type-tests to `match` and discriminated unions
- `.Result` to proper `async`/`task` (see `fsharp-async-and-tasks` for depth)

### Step 3: Tighten pattern matching

Make matches exhaustive over closed types and remove stray wildcards, so the compiler warns
when a new case is added later.

### Step 4: Apply open hygiene

Keep `open` statements minimal and close to use. Suggest `[<RequireQualifiedAccess>]` on
modules whose names would collide (for example a custom `Result`-like module).

### Step 5: Verify it compiles

Round-trip the rewrite through F# Interactive or the build so it actually compiles:

```bash
dotnet fsi rewrite.fsx
# or, inside a project:
dotnet build
```

See the `fsharp-scripts` skill for the `.fsx` workflow.

## Quick reference: highest-value rewrites

| C#-ism | Idiomatic F# |
|--------|--------------|
| `let mutable sum = 0` + `for x in xs do sum <- sum + x` | `xs \|> List.sum` |
| `let mutable acc = []` + loop with `acc <- f x :: acc` | `xs \|> List.map f` |
| `if x <> null then f x else d` | `x \|> Option.map f \|> Option.defaultValue d` |
| `let mutable r = 0` + `if c then r <- a else r <- b` | `let r = if c then a else b` |
| `g(f(x))` | `x \|> f \|> g` |
| nested `if/else if` over a closed set | `match value with ...` |

## Validation

- [ ] No `mutable` remains except where mutation is deliberate and justified
- [ ] No `for`/`while` loop that merely builds a value (replaced by `map`/`filter`/`fold`)
- [ ] No `null` on F# types (replaced by `Option`)
- [ ] `if`/`match` used as expressions, not statements
- [ ] Matches over closed types are exhaustive (no stray `_`)
- [ ] The rewrite compiles (`dotnet fsi` or `dotnet build`)

## Common Pitfalls

| Pitfall | Correction |
|---------|------------|
| Making everything point-free with `>>` | Keep `>>` only where it stays readable; use named pipelines otherwise |
| Removing every `_` blindly | Wildcards are fine for genuinely open inputs; only closed, enumerable cases must be explicit |
| "Idiomatic" rewrite that no longer compiles | Always round-trip through `dotnet fsi` / `dotnet build` |
| Replacing a deliberate `mutable` hot path | Respect performance-motivated mutation; this skill targets defaults, not micro-optimized code |
| Over-`open`-ing to shorten names | Prefer `[<RequireQualifiedAccess>]` plus a local `open` |
| Deep nested pipelines no one can read | Break into named intermediate bindings with meaningful names |

## More info

- F# style guide: https://learn.microsoft.com/en-us/dotnet/fsharp/style-guide/
- F# coding conventions: https://learn.microsoft.com/en-us/dotnet/fsharp/style-guide/conventions
