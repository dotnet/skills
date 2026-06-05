---
name: testing-fsharp
description: "Write idiomatic F# tests, including property-based tests. Use when adding unit tests for F# code, choosing an F# test framework (Expecto, or xUnit/NUnit with FsUnit), or introducing property-based testing with FsCheck. Covers Expecto test lists, xUnit-with-F# style, FsUnit assertions, and FsCheck properties/generators. Do not use for general .NET test running, filtering, or migration - use the dotnet-test plugin skills for those."
license: MIT
---

# Testing F#

## Purpose

Write tests that read naturally in F# and exploit F#'s strengths - especially property-based
testing, which checks invariants across many generated inputs instead of a handful of examples.

## When to Use

- Adding unit tests for F# code
- Choosing an F# test approach (Expecto vs xUnit/NUnit + FsUnit)
- Introducing property-based tests with FsCheck

## When Not to Use

- Running/filtering/migrating .NET tests generally - use the `dotnet-test` plugin skills
- Pure C# test projects

## Approach 1: Expecto (F#-first)

Expecto models tests as plain values (`test "..." { ... }`) in a `testList`, run from `main`.

```fsharp
open Expecto

let tests =
    testList "math" [
        test "addition is commutative for two examples" {
            Expect.equal (2 + 3) (3 + 2) "should be equal"
        }
        test "list reverse twice is identity" {
            let xs = [ 1; 2; 3 ]
            Expect.equal (xs |> List.rev |> List.rev) xs "round-trips"
        }
    ]

[<EntryPoint>]
let main argv = runTestsWithCLIArgs [] argv tests
```

## Approach 2: xUnit/NUnit + FsUnit

Familiar if the rest of the solution uses xUnit. FsUnit gives F#-readable assertions.

```fsharp
open Xunit
open FsUnit.Xunit

[<Fact>]
let ``reversing twice returns the original`` () =
    [ 1; 2; 3 ] |> List.rev |> List.rev |> should equal [ 1; 2; 3 ]
```

Backtick-quoted names give readable test descriptions.

## Property-based testing with FsCheck

Instead of fixed examples, state a property that must hold for **all** inputs; FsCheck generates
many cases (and shrinks failures to a minimal counterexample).

```fsharp
open FsCheck

let ``reverse twice is identity`` (xs: int list) =
    List.rev (List.rev xs) = xs

Check.Quick ``reverse twice is identity``
```

With Expecto, use `testProperty`:

```fsharp
testProperty "addition is commutative" (fun (a: int) (b: int) -> a + b = b + a)
```

Good properties to look for: round-trips (encode/decode), invariants (length preserved),
commutativity/associativity, and equivalence to a simple reference implementation.

### Custom generators

When the default generator produces invalid inputs, constrain it:

```fsharp
let positiveInts = Arb.generate<int> |> Gen.filter (fun n -> n > 0) |> Arb.fromGen
```

## Workflow

1. Choose Expecto (F#-first) or xUnit/NUnit + FsUnit (matches existing solution).
2. Write example-based tests for specific known cases.
3. Add property-based tests for invariants/round-trips with FsCheck.
4. Add custom generators where the domain restricts valid inputs.
5. Run with `dotnet test` (or the Expecto runner) and confirm green; see the `dotnet-test`
   skills for running and filtering.

## Validation

- [ ] Tests compile and run green
- [ ] Invariants/round-trips covered by property-based tests, not just examples
- [ ] Generators restricted where the domain requires valid inputs
- [ ] Test names read as clear descriptions

## Common Pitfalls

| Pitfall | Correction |
|---------|------------|
| Only example-based tests for code with clear invariants | Add FsCheck properties |
| Property fails on inputs the code never accepts | Add a custom generator / precondition |
| Mixing frameworks arbitrarily across a solution | Pick one approach per test project |
| Re-deriving the implementation inside the property | Compare against a simpler reference or an invariant |

## More info

- Unit testing in .NET: https://learn.microsoft.com/en-us/dotnet/core/testing/
- FsCheck: https://fscheck.github.io/FsCheck/
- Expecto: https://github.com/haf/expecto
