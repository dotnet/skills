---
name: fsharp-scripts
description: "Run F# scripts (.fsx) with F# Interactive (dotnet fsi) when the user wants to experiment with F# without creating a project. Use for trying an F# language feature or API, prototyping logic before integrating it, one-file F# utilities, or referencing NuGet packages and other scripts inline. Covers dotnet fsi, #r \"nuget:\", #load, #r for DLLs, fsi.CommandLineArgs, #time, and %A printing. Do not use for full projects/solutions, language-agnostic throwaway scripts, or integrating code into an existing .NET solution."
license: MIT
---

# F# Scripts with F# Interactive

## When to Use

- Testing an F# concept, API, or language feature quickly
- Prototyping logic before integrating it into a larger project
- Building a small one-file utility
- Exploring a NuGet package interactively

## When Not to Use

- The user asks for a language-agnostic quick script or throwaway computation
- The user needs a full project, solution integration, or library
- The user is working inside an existing .NET solution and wants to add code there

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| F# code or intent | Yes | The code to run, or a description of what the script should do |

## Workflow

### Step 1: Check the .NET SDK

```bash
dotnet --version
```

F# Interactive ships with the .NET SDK. `dotnet fsi` is available with any modern SDK; no extra
install is required.

### Step 2: Write the script

Create a `.fsx` file. Top-level code runs in order, top to bottom.

```fsharp
// hello.fsx
let numbers = [ 1; 2; 3; 4; 5 ]
let sum = numbers |> List.sum
printfn "Sum: %d" sum
```

`.fsx` is a script (runs directly). `.fs` is a compiled source file (belongs to a project). Use
`.fsx` here.

### Step 3: Run it

```bash
dotnet fsi hello.fsx
```

Pass arguments after the script path and read them with `fsi.CommandLineArgs`:

```bash
dotnet fsi hello.fsx -- alpha beta
```

```fsharp
let args = fsi.CommandLineArgs       // args.[0] is the script name
printfn "%A" args.[1..]
```

### Step 4: Reference packages and files (directives)

Script directives start with `#`. Place them at the top.

#### `#r "nuget:"` - NuGet packages

```fsharp
#r "nuget: FSharp.Data, 6.4.0"
open FSharp.Data
```

Omit the version to take the latest, but pin a version for reproducibility.

#### `#r` - a DLL by path

```fsharp
#r "../bin/Debug/net10.0/MyLib.dll"
```

#### `#load` - another script or source file

```fsharp
#load "Helpers.fsx"
Helpers.greet "world"
```

`#load` brings the file's definitions into the session. Loaded files run in order.

#### `#time "on"` - timing

```fsharp
#time "on"
// subsequent evaluations print real/CPU time
```

### Step 5: Print results

- `printfn "%d / %s / %f"` - typed format specifiers (compiler-checked)
- `%A` - structured pretty-print for any F# value (records, DUs, lists)
- `printfn "%A" value` is the quickest way to inspect domain data

### Step 6: Clean up

Remove the `.fsx` files when the user is done.

## Unix shebang support

Make a `.fsx` directly executable on Unix:

```fsharp
#!/usr/bin/env -S dotnet fsi
printfn "I'm executable!"
```

```bash
chmod +x hello.fsx
./hello.fsx
```

Use `LF` line endings (not `CRLF`) for shebang scripts.

## Interactive REPL

Start a bare REPL with `dotnet fsi`. In the REPL, terminate an expression with `;;` to evaluate
it. `#help;;` lists directives; `#quit;;` exits.

## Validation

- [ ] `dotnet --version` succeeds (SDK present)
- [ ] The script is a `.fsx` file with top-level code
- [ ] `dotnet fsi <file>.fsx` produces the expected output
- [ ] Any `#r "nuget:"` reference resolves and the package is usable
- [ ] Multi-file scripts wire up with `#load` and run in order
- [ ] Script files are cleaned up after the session

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Using `.fs` instead of `.fsx` | Scripts run with `dotnet fsi` must be `.fsx` |
| `#r "nuget:"` without internet/feed | Ensure NuGet feed access; pin a version for reproducibility |
| Directives placed after code | All `#` directives go at the top of the file |
| Forward references | F# scripts run top to bottom; define before use |
| `CRLF` on a shebang script | Use `LF` line endings; the shebang is ignored on Windows |
| Expecting `Main` / namespaces | Scripts use top-level statements; namespaces are not available in `.fsx` |

## More info

- F# Interactive: https://learn.microsoft.com/en-us/dotnet/fsharp/tools/fsharp-interactive/
