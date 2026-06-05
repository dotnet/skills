---
name: fsharp-domain-modeling
description: "Model domains in F# with the type system so illegal states are unrepresentable. Use when designing F# types, replacing primitive obsession (raw strings/ints/bools), removing boolean flags, modeling state machines, or replacing class hierarchies with discriminated unions. Covers records vs DUs, single-case DUs with private constructors and smart constructors (create/value), flags-to-DUs, DUs over inheritance for tree data, and RequireQualifiedAccess. Do not use for trivial DTOs that never evolve, or for shaping types for C# consumers (use design-fsharp-for-dotnet-interop)."
license: MIT
---

# F# Domain Modeling

## Purpose

Use F#'s type system - records, discriminated unions, and single-case wrappers - so that
invalid data cannot be constructed in the first place. The compiler, not runtime validation,
enforces the rules.

## When to Use

- Designing types for a new domain or feature
- Replacing primitive obsession: raw `string`/`int`/`bool` standing in for real concepts
- Removing boolean flags that encode state
- Modeling a workflow or entity that moves through distinct states
- Replacing a class hierarchy used for tree-like or variant data

## When Not to Use

- Trivial, stable DTOs that will never grow rules (a plain record is enough)
- Types whose public surface must be consumed from C# - use
  `design-fsharp-for-dotnet-interop`

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Domain description or existing types | Yes | What is being modeled, or the types to improve |
| Invariants / rules | No | Constraints the types should make impossible to violate |

## Core techniques

### 1. Records for "AND", discriminated unions for "OR"

A record groups values that all coexist. A DU expresses a choice between alternatives.

```fsharp
type Customer = { Name: string; Email: EmailAddress }           // name AND email
type Contact = Email of EmailAddress | Phone of PhoneNumber      // email OR phone
```

### 2. Single-case DU + private constructor = a constrained type

Wrap a primitive so a value of the type is provably valid (the "smart constructor" pattern).
Put `create` (validation) and `value` (extraction) in a module with the same name.

```fsharp
type EmailAddress = private EmailAddress of string

[<RequireQualifiedAccess>]
module EmailAddress =
    let create (s: string) =
        if s.Contains "@" then Ok (EmailAddress s)
        else Error "email must contain '@'"

    let value (EmailAddress s) = s
```

Outside the module the only way to get an `EmailAddress` is through `create`, so every
`EmailAddress` in the system is valid by construction.

### 3. Replace boolean flags with a discriminated union

Booleans lose meaning and combine into impossible states. Name the states.

```fsharp
// Instead of: { IsVerified: bool; IsSuspended: bool }  (4 combos, some nonsensical)
type AccountState =
    | Unverified
    | Active
    | Suspended of reason: string
```

### 4. Make illegal states unrepresentable: group what changes together

If two optional fields are only ever both present or both absent, model that.

```fsharp
// Instead of: { Email: string option; VerifiedAt: DateTime option }
type EmailStatus =
    | NotProvided
    | Provided of EmailAddress
    | Verified of EmailAddress * DateTime
```

### 5. DUs over class hierarchies for tree-structured / variant data

```fsharp
type Expr =
    | Const of int
    | Add of Expr * Expr
    | Mul of Expr * Expr
```

Recursive variants are awkward with inheritance and elegant with DUs, and pattern matching
stays exhaustive.

## Workflow

1. List the concepts and their invariants.
2. For each "a value that must satisfy a rule", make a single-case DU with a private
   constructor and a `create` that returns `Result`.
3. For each "one of several shapes", make a DU; for "several fields together", a record.
4. Collapse boolean flags and parallel `option` fields into DUs that name the real states.
5. Verify: try to write code that constructs an illegal value - it should not compile.

## Validation

- [ ] No raw `string`/`int` for concepts with rules (wrapped in constrained types)
- [ ] No public constructor that can build an invalid value (private + `create`)
- [ ] Boolean flags encoding state replaced by named DU cases
- [ ] Parallel `option` fields that vary together collapsed into one DU
- [ ] Tree/variant data modeled as a DU, not an inheritance hierarchy
- [ ] Types compile and illegal construction is rejected by the compiler

## Common Pitfalls

| Pitfall | Correction |
|---------|------------|
| Validation scattered at call sites | Centralize it in the type's `create` smart constructor |
| Public single-case DU constructor | Mark it `private` so validation cannot be bypassed |
| `bool` parameters that encode a mode | Replace with a small DU; reads at the call site |
| Throwing from `create` | Return `Result` so callers handle invalid input (see `fsharp-error-handling`) |
| DU case names collide across types | Add `[<RequireQualifiedAccess>]` to the type |

## More info

- Designing with types: https://learn.microsoft.com/en-us/dotnet/fsharp/style-guide/conventions
- Discriminated unions: https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/discriminated-unions
