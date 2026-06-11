---
name: fsharp-error-handling
description: "Handle expected errors in F# with Result and Option instead of exceptions. Use when adding error handling, input validation, or fallible workflows, or when refactoring exception-driven F# to functional error flow. Covers Result vs Option vs exceptions, Result.bind/map/mapError, the result computation expression, railway-oriented chaining, applicative validation that accumulates multiple errors, Result.sequence/traverse over collections, and tryX vs Xexn naming. Do not use for genuinely exceptional/panic paths or the outermost boundary where exceptions are acceptable."
license: MIT
---

# F# Error Handling

## Purpose

Represent expected, recoverable errors as values (`Result`, `Option`) that the compiler forces
callers to handle - reserving exceptions for the truly exceptional.

## When to Use

- Adding error handling to a function or workflow
- Validating input, especially when multiple fields can each be invalid
- Chaining steps where any step can fail
- Refactoring `try/with`-driven code that uses exceptions for normal control flow

## When Not to Use

- Genuinely exceptional, unrecoverable conditions (programmer errors, invariant violations)
- The outermost boundary (e.g. a top-level handler) where catching an exception is the right
  move
- Async/Task error propagation specifics - see `fsharp-async-and-tasks`

## Choosing the representation

| Situation | Use |
|-----------|-----|
| Operation can fail and the caller needs to know **why** | `Result<'T, 'Error>` |
| Value may be absent and the reason does not matter | `Option<'T>` |
| Truly unexpected / unrecoverable | exception |

Name functions accordingly: `tryParse` returns `Option`/`Result`; a throwing variant is named
`parseExn` (or documents that it throws).

## Core techniques

### 1. Return Result instead of throwing

```fsharp
let divide x y =
    if y = 0 then Error "division by zero"
    else Ok (x / y)
```

### 2. Chain fallible steps with bind (railway-oriented)

Each step runs only if the previous succeeded; the first `Error` short-circuits.

```fsharp
let placeOrder rawInput =
    rawInput
    |> validateInput
    |> Result.bind checkInventory
    |> Result.bind chargePayment
    |> Result.map buildConfirmation
```

### 3. The result computation expression reads better than bind chains

```fsharp
let placeOrder rawInput =
    result {
        let! valid = validateInput rawInput
        let! stocked = checkInventory valid
        let! charged = chargePayment stocked
        return buildConfirmation charged
    }
```

`result { }` ships in libraries such as FsToolkit.ErrorHandling, or can be authored with
`authoring-computation-expressions`.

### 4. Accumulate multiple errors with applicative validation

`bind` short-circuits on the first error. For form validation you usually want **all** the
errors. Use a `Validation` (a `Result` whose error side is a list) and apply fields together.

```fsharp
// validateName : Input -> Validation<string, string>
// validateAge  : Input -> Validation<int, string>
let validateForm input =
    validation {
        let! name = validateName input
        and! age = validateAge input
        return { Name = name; Age = age }
    }
// both validators run; errors collected into a list
```

`and!` (not `let!`) is what makes the validators independent and accumulating.
`validation { }` and the `Validation` type come from FsToolkit.ErrorHandling, not FSharp.Core.

### 5. Flip a list of Results: sequence / traverse

Turn `Result list` into a single `Result` of a list (fails if any element fails).

```fsharp
let parseAll lines =
    lines
    |> List.map parseLine          // string list -> Result<Row,string> list
    |> List.sequenceResultM        // -> Result<Row list, string>
```

`List.sequenceResultM` (and `traverseResultM`) come from FsToolkit.ErrorHandling, not FSharp.Core.

### 6. Convert exceptions at the boundary

When calling a .NET API that throws, catch once and convert to `Result`:

```fsharp
let readConfig path =
    try Ok (System.IO.File.ReadAllText path)
    with :? System.IO.IOException as ex -> Error ex.Message
```

## Workflow

1. Decide `Result` vs `Option` vs exception for each fallible operation.
2. Make functions return that value instead of throwing.
3. Compose steps with `result { }` (sequential) or `validation { }` with `and!` (accumulating).
4. For collections of fallible results, use `sequence`/`traverse`.
5. Wrap throwing .NET calls at the edges; let `Result` flow inward.
6. Verify the happy path and at least one failing path with `dotnet fsi`.

## Validation

- [ ] Expected errors are `Result`/`Option`, not exceptions
- [ ] Chains use `Result.bind`/`result { }`, not nested matches
- [ ] Multi-field validation accumulates errors with `and!` (not first-error short-circuit)
- [ ] Collections of results handled with `sequence`/`traverse`
- [ ] Throwing .NET APIs converted to `Result` at the boundary
- [ ] `tryX` vs `Xexn` naming reflects whether a function can throw

## Common Pitfalls

| Pitfall | Correction |
|---------|------------|
| Using `bind` for form validation | Use applicative `validation { }` with `and!` to collect all errors |
| `Option` where the caller needs the reason | Use `Result` with a descriptive error |
| Stringly-typed errors everywhere | Consider a DU error type (see `fsharp-domain-modeling`) |
| `failwith` for ordinary validation | Return `Error`; reserve exceptions for the unexpected |
| Catching `System.Exception` broadly | Catch the specific exception type you can handle |

## More info

- Error management conventions: https://learn.microsoft.com/en-us/dotnet/fsharp/style-guide/conventions
- Result type: https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/results
