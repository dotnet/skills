---
name: design-fsharp-for-dotnet-interop
description: "Shape an F# public API so it is natural to consume from C# and other .NET languages (a vanilla .NET library). Use when an F# library, assembly, or layer is consumed by C#/VB, or when reviewing an F# public surface for interop friction. Covers hiding discriminated unions, exposing seq/IDictionary instead of F# list/Map, Func/Action instead of F# functions, Task instead of Async, the TryGetValue pattern instead of option, CompiledName, CLIEvent, and null checks at the boundary. Do not use for F#-to-F# internal code (use writing-idiomatic-fsharp / fsharp-domain-modeling)."
license: MIT
---

# Designing F# for .NET Interop

## Purpose

F#-idiomatic types (`Async`, F# functions, `list`, `Map`, `option`, discriminated unions)
appear awkward or alien to C# consumers. This skill shapes the **public** surface of an F#
component so it follows .NET Library Design Guidelines and feels native from C#/VB - while the
implementation stays idiomatic F# internally.

## When to Use

- An F# library/assembly is consumed by C# or other .NET languages
- Reviewing an F# public API for interop friction
- Publishing an F# NuGet package for general .NET use

## When Not to Use

- F#-to-F# internal code - keep it idiomatic (`writing-idiomatic-fsharp`,
  `fsharp-domain-modeling`)

## The interop rules

Apply these at the **public boundary** only; implement internally however is idiomatic.

| F#-idiomatic (internal) | C#-friendly (public surface) |
|-------------------------|------------------------------|
| `namespace` + modules with `let` functions | `namespace` + `[<AbstractClass; Sealed>]` static-member types |
| F# `list<T>`, `Map`, `Set` | `seq<T>` (`IEnumerable<T>`), `IDictionary<K,V>` |
| F# function `int -> int` | `System.Func<int,int>` / `System.Action<...>` |
| `Async<T>` | `Task<T>` (via `Async.StartAsTask`), with a `CancellationToken` overload |
| returns `option<T>` | `bool` + `out` param (`TryGetValue` pattern) |
| takes `option<T>` | method overloads or optional arguments |
| public discriminated union | hidden (`private`/signature file) + members / active patterns |
| curried params `f a b` | tupled params `Method(a, b)` |
| F# `Event` | `DelegateEvent` + `[<CLIEvent>]` |

### Static members instead of module functions

```fsharp
namespace Fabrikam

[<AbstractClass; Sealed>]
type Utilities =
    static member Add(x, y) = x + y
    static member Add(x, y, z) = x + y + z
```

Static types allow overloading and future evolution; modules compile to a shape C# cannot use
as cleanly.

### Hide DUs; expose members or active patterns

```fsharp
type Shape =
    private
    | Circle of float
    | Rect of float * float

    static member CreateCircle r = Circle r
    member this.Area =
        match this with
        | Circle r -> System.Math.PI * r * r
        | Rect (w, h) -> w * h
```

### Async to Task at the boundary

```fsharp
type Service() =
    let computeAsync x = async { return x + 1 }     // idiomatic internally
    member _.ComputeAsync(x, ct) =
        Async.StartAsTask(computeAsync x, cancellationToken = ct)
```

### CompiledName for .NET-friendly names

```fsharp
type Vector(x: float, y: float) =
    member _.X = x
    [<CompiledName("Create")>]
    static member create x y = Vector(x, y)
```

### Check for null at the boundary

C# callers pass `null` freely; validate before it reaches F# code.

```fsharp
let checkNonNull name (arg: obj) =
    match arg with
    | null -> nullArg name
    | _ -> ()
```

## Workflow

1. Separate the public surface from the implementation (an `Api` type/module is a good seam).
2. Replace F# collection types with `seq`/`IDictionary` on signatures.
3. Replace F# function parameters with `Func`/`Action`.
4. Convert `Async<T>` returns to `Task<T>`; add a `CancellationToken` overload.
5. Replace `option` returns with `TryGetValue`; `option` params with overloads.
6. Hide DUs; expose members/active patterns/factory methods. Use tupled, not curried, params.
7. Add `null` checks at the boundary. Optionally add an `.fsi` to lock the surface.
8. Verify the shape - ideally compile a small C# caller, or inspect with the object browser.

## Validation

- [ ] Public signatures use `seq`/`IDictionary`, not F# `list`/`Map`/`Set`
- [ ] Public delegates are `Func`/`Action`, not F# function types
- [ ] Async members return `Task<T>` with a cancellation overload
- [ ] No public `option` returns (TryGetValue) or `option` params (overloads)
- [ ] Public DUs are hidden behind members/active patterns
- [ ] Public methods use tupled parameters
- [ ] `null` is checked at the boundary

## Common Pitfalls

| Pitfall | Correction |
|---------|------------|
| Exposing module `let` functions to C# | Use a static-member type (`[<AbstractClass; Sealed>]`) |
| Returning F# `list`/`Map` | Return `seq`/`IDictionary` |
| Public `Async<T>` | Return `Task<T>` via `Async.StartAsTask` |
| Public DU consumed by C# | Hide it; expose members or `CreateX` factories |
| Curried public method | Use tupled `Method(a, b)` |

## More info

- Component design guidelines: https://learn.microsoft.com/en-us/dotnet/fsharp/style-guide/component-design-guidelines
- .NET library design guidelines: https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/
