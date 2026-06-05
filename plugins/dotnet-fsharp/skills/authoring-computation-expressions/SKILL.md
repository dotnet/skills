---
name: authoring-computation-expressions
description: "Author custom F# computation expression builders (the result {}, option {}, async {} style of block). Use when you want a let!/return DSL for a wrapper type such as Result or Option, to remove repetitive bind/map chains, or to build a small workflow builder. Covers the builder class, the core members (Bind, Return, ReturnFrom, Zero, Combine, Delay, For, While, Using, TryWith), and a minimal result/option builder. Do not use for merely consuming existing computation expressions, or where a plain pipeline of bind/map is already clear."
license: MIT
---

# Authoring F# Computation Expressions

## Purpose

A computation expression (CE) is a builder object that gives a type a `let!`/`return` block
syntax. Authoring one lets you replace repetitive `bind`/`map` chains with readable sequential
code for your own wrapper types.

## When to Use

- You repeatedly chain `Result.bind` / `Option.bind` and want `let!`/`return` instead
- You have a custom wrapper/effect type that would benefit from block syntax
- You are building a small internal DSL (parsers, builders, pipelines)

## When Not to Use

- You only need to **use** an existing CE (`async`, `task`, `result`) - just use it
- A single `bind`/`map` pipeline is already clear; a CE adds indirection
- A library already provides the builder (e.g. FsToolkit.ErrorHandling's `result`/`validation`)

## How a CE works

`builder { ... }` desugars to method calls on a builder instance:

| Syntax | Builder member |
|--------|----------------|
| `let! x = e in rest` | `Bind(e, fun x -> rest)` |
| `return x` | `Return(x)` |
| `return! e` | `ReturnFrom(e)` |
| (empty / `if` with no else) | `Zero()` |
| two statements in sequence | `Combine(a, b)` |
| delayed evaluation | `Delay(fun () -> ...)` |
| `for x in xs do ...` | `For(xs, body)` |
| `while cond do ...` | `While(guard, body)` |
| `use x = e in ...` | `Using(e, body)` |
| `try ... with` | `TryWith(body, handler)` |

You only implement the members your block actually uses.

## A minimal result builder

```fsharp
type ResultBuilder() =
    member _.Bind(result, f) = Result.bind f result
    member _.Return(value) = Ok value
    member _.ReturnFrom(result) = result
    member _.Zero() = Ok ()

let result = ResultBuilder()

// usage
let compute a b =
    result {
        let! x = if a > 0 then Ok a else Error "a must be positive"
        let! y = if b > 0 then Ok b else Error "b must be positive"
        return x + y
    }
```

`let!` chains via `Bind`; the first `Error` short-circuits the rest of the block.

## A minimal option builder

```fsharp
type OptionBuilder() =
    member _.Bind(opt, f) = Option.bind f opt
    member _.Return(value) = Some value
    member _.ReturnFrom(opt) = opt

let option = OptionBuilder()
```

## Adding more members

- Add `Zero` to allow `if cond then return! x` with no `else`.
- Add `Combine` + `Delay` to allow multiple statements / early constructs.
- Add `For`/`While` only if the block needs loops.
- Add `Using`/`TryWith`/`TryFinally` for resource and exception handling inside the block.

Implement incrementally: the compiler error names the missing member when a block needs one.

## Workflow

1. Identify the wrapper type and the repetitive `bind`/`map` chain.
2. Write a builder class with `Bind` and `Return` (and `ReturnFrom`/`Zero` as needed).
3. Instantiate it as a lowercase value (`let result = ResultBuilder()`).
4. Rewrite the chain as a `builder { ... }` block.
5. Add further members only when a compiler error asks for one.
6. Verify with `dotnet fsi`.

## Validation

- [ ] Builder implements at least `Bind` and `Return` with correct types
- [ ] The CE block compiles and behaves like the underlying `bind`/`map` chain
- [ ] Short-circuiting / sequencing matches the wrapper's semantics
- [ ] No CE authored where an existing one (or a plain pipeline) would do

## Common Pitfalls

| Pitfall | Correction |
|---------|------------|
| Re-implementing `result`/`validation` that a library already ships | Use FsToolkit.ErrorHandling instead |
| Wrong `Bind` signature | `Bind: M<'a> * ('a -> M<'b>) -> M<'b>` for the wrapper `M` |
| Block needs `Zero`/`Combine` but they are missing | Add the member the compiler error names |
| Authoring a CE for a one-off two-step chain | Just use `bind`/`map` directly |

## More info

- Computation expressions: https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/computation-expressions
