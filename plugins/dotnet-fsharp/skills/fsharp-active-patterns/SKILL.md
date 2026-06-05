---
name: fsharp-active-patterns
description: "Use and author F# active patterns to make pattern matching expressive over data that is not a plain discriminated union. Use when classifying or destructuring values (parsing, matching on .NET types, ranges, regex captures) leads to nested if/match, or when you want named, reusable match cases. Covers total active patterns (|A|B|), partial patterns (|A|_|), parameterized patterns, and when to prefer them over plain match. Do not use for data already well modeled as a DU (just match it directly)."
license: MIT
---

# F# Active Patterns

## Purpose

Active patterns let you pattern-match over values whose shape is not already a discriminated
union - converting `int`, `string`, .NET objects, or computed classifications into named,
matchable cases. They turn nested conditionals into readable `match` expressions.

## When to Use

- Classifying a value into named buckets (even/odd, ranges, categories)
- Parsing: turning a `string` into a structured result inside a `match`
- Matching over .NET types you do not own
- Reusing the same classification logic across several `match` expressions

## When Not to Use

- The data is already a DU - match it directly, no active pattern needed
- A single inline `if` is clearer than introducing a named pattern

## The three forms

### 1. Total (complete) active pattern - covers all inputs

```fsharp
let (|Even|Odd|) n = if n % 2 = 0 then Even else Odd

let describe n =
    match n with
    | Even -> "even"
    | Odd -> "odd"
```

A total pattern partitions the input into a fixed set of cases. The match is exhaustive.

### 2. Partial active pattern - may not match (returns Option)

Use when the value only sometimes fits the case. The name ends with `|_|`.

```fsharp
let (|Int|_|) (s: string) =
    match System.Int32.TryParse s with
    | true, v -> Some v
    | _ -> None

let parse s =
    match s with
    | Int v -> sprintf "number %d" v
    | _ -> "not a number"
```

### 3. Parameterized active pattern - takes extra arguments

```fsharp
let (|DivisibleBy|_|) divisor n =
    if n % divisor = 0 then Some () else None

let fizzbuzz n =
    match n with
    | DivisibleBy 15 -> "FizzBuzz"
    | DivisibleBy 3 -> "Fizz"
    | DivisibleBy 5 -> "Buzz"
    | _ -> string n
```

### Multiple captures with one partial pattern

```fsharp
open System.Text.RegularExpressions

let (|Regex|_|) pattern input =
    let m = Regex.Match(input, pattern)
    if m.Success then Some [ for g in m.Groups -> g.Value ]
    else None

let parseDate s =
    match s with
    | Regex @"(\d{4})-(\d{2})-(\d{2})" [ _; y; mo; d ] -> Some (y, mo, d)
    | _ -> None
```

## Workflow

1. Spot the nested `if`/`match` or repeated classification logic.
2. Choose the form: total (always one of N cases) or partial (`|_|`, may not match).
3. Add parameters if the test needs configuration.
4. Replace the conditional with a `match` over the new pattern(s).
5. Verify with `dotnet fsi`.

## Validation

- [ ] Nested conditionals replaced by a `match` over active patterns
- [ ] Partial patterns end in `|_|` and return `Option`
- [ ] Total patterns enumerate all cases and keep the match exhaustive
- [ ] The code compiles and matches as expected (`dotnet fsi`)

## Common Pitfalls

| Pitfall | Correction |
|---------|------------|
| Active pattern for data already a DU | Match the DU directly |
| Total pattern that cannot actually cover all inputs | Make it a partial pattern (`|_|`) returning `Option` |
| Heavy work inside a frequently matched pattern | Active patterns run on each match; keep them cheap or memoize |
| Too many tiny patterns | Reserve them for genuinely reused or clarifying classifications |

## More info

- Active patterns: https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/active-patterns
