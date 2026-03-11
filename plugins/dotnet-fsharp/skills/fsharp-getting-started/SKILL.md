---
name: fsharp-getting-started
description: >
  Create and work with F# projects in .NET: project setup, idiomatic types, and core language patterns.
  USE FOR: creating new F# console apps, libraries, or test projects with `dotnet new`; writing
  discriminated unions, records, pattern matching, pipe operators, and async computation expressions;
  understanding F# project structure and module conventions; choosing between F# and C# for a given task.
  DO NOT USE FOR: C#-only projects, advanced F# type providers, or MSBuild/build pipeline configuration.
---

# Getting Started with F#

Get up and running with F# on .NET: create a project, understand the key language constructs, and write
idiomatic functional code from day one.

## When to Use

- User wants to create a new F# console app, library, or test project
- User is learning F# and needs guidance on idiomatic patterns
- User wants to use discriminated unions, records, active patterns, or computation expressions
- User needs to understand how F# modules, namespaces, and files are organized
- User is evaluating whether F# is a good fit for their use case

## When Not to Use

- The project is already C# and the user wants C#-specific guidance
- The user needs advanced F# type providers (requires a dedicated skill)
- The issue is a build or MSBuild problem (use dotnet-msbuild skills)
- The user wants F#-to-C# interoperability patterns (consider a dedicated interop skill)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Project type | Yes | console, classlib, mstest, nunit, or xunit |
| Target framework | No | Defaults to the current LTS (e.g., `net9.0`) |
| Project name | No | Kebab-case name for the new project |

## Workflow

### Step 1: Create the F# project

Use `dotnet new` with the `-lang F#` flag:

```bash
# Console application
dotnet new console -lang F# -n MyApp -o ./MyApp

# Class library
dotnet new classlib -lang F# -n MyLib -o ./MyLib

# xUnit test project
dotnet new xunit -lang F# -n MyApp.Tests -o ./MyApp.Tests
```

For a solution with multiple projects:

```bash
dotnet new sln -n MyApp
dotnet sln add ./MyApp/MyApp.fsproj
dotnet sln add ./MyApp.Tests/MyApp.Tests.fsproj
```

### Step 2: Understand the project structure

An F# project (`.fsproj`) lists source files **in compilation order** — unlike C#, the order matters:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <!-- Files are compiled top-to-bottom; each file can only use names from files above it -->
    <Compile Include="Domain.fs" />
    <Compile Include="Services.fs" />
    <Compile Include="Program.fs" />
  </ItemGroup>
</Project>
```

### Step 3: Write idiomatic F# types

Prefer F# records and discriminated unions over classes:

```fsharp
// Immutable record — use instead of a data-only class
type Customer = {
    Id: int
    Name: string
    Email: string
}

// Discriminated union — model a fixed set of alternatives
type Shape =
    | Circle of radius: float
    | Rectangle of width: float * height: float
    | Triangle of base': float * height: float

// Compute area with exhaustive pattern matching
let area shape =
    match shape with
    | Circle r        -> System.Math.PI * r * r
    | Rectangle(w, h) -> w * h
    | Triangle(b, h)  -> 0.5 * b * h
```

### Step 4: Use the pipe operator for readable data transformations

Chain operations left-to-right with `|>` instead of nesting calls:

```fsharp
// Idiomatic F#: data flows through a pipeline
let result =
    [1..20]
    |> List.filter (fun x -> x % 2 = 0)
    |> List.map (fun x -> x * x)
    |> List.sum   // 1540

// Equivalent nested form (avoid in F#):
// List.sum (List.map (fun x -> x * x) (List.filter (fun x -> x % 2 = 0) [1..20]))
```

### Step 5: Handle optional values with `option` and `Result`

Avoid nulls — use F# option types and result types instead:

```fsharp
// option<'T> — value may or may not exist
let tryFindById (id: int) (customers: Customer list) : Customer option =
    customers |> List.tryFind (fun c -> c.Id = id)

// Result<'T, 'TError> — success or structured failure
let validateEmail (email: string) : Result<string, string> =
    if email.Contains('@') then Ok email
    else Error $"Invalid email: {email}"

// Compose with pattern matching
let processCustomer id customers =
    match tryFindById id customers with
    | None          -> printfn "Customer %d not found" id
    | Some customer ->
        match validateEmail customer.Email with
        | Error msg -> printfn "Validation failed: %s" msg
        | Ok email  -> printfn "Processing %s <%s>" customer.Name email
```

### Step 6: Write async code with computation expressions

Use `async { }` for .NET async work (or `task { }` for direct `Task<T>` interop):

```fsharp
open System.Net.Http

// async workflow — F#-native; use Async.RunSynchronously or pipe to Async.StartAsTask
let fetchAsync (url: string) =
    async {
        use client = new HttpClient()
        let! response = client.GetStringAsync(url) |> Async.AwaitTask
        return response.Length
    }

// task workflow — directly returns Task<T>, easier for .NET interop
let fetchTask (url: string) =
    task {
        use client = new HttpClient()
        let! response = client.GetStringAsync(url)
        return response.Length
    }
```

## Validation

- [ ] Project builds with `dotnet build` without errors or warnings
- [ ] Core types (records, DUs) are defined before they are used in the file list
- [ ] Pattern matching is exhaustive — the compiler warns on missing cases
- [ ] No mutable state or nulls introduced without explicit intent (`let mutable`, `Nullable`)
- [ ] Tests pass with `dotnet test`

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| File order in `.fsproj` is wrong | F# compiles top-to-bottom; move the defining file above the using file |
| Using `null` instead of `option` | Use `option<'T>` and pattern match; reserve `null` only for .NET interop |
| Shadowing a binding with `let` | Intentional in F#, but rename to avoid confusion when it is accidental |
| Mixing `async` and `task` workflows | Pick one style per project; prefer `task { }` when calling .NET APIs |
| Forgetting `rec` on recursive functions | Add `let rec` (or `let rec … and …` for mutually recursive functions) |
| Class-heavy design from C# habits | Prefer modules + functions + records/DUs over classes in F# |
