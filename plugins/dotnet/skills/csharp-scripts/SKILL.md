---
name: csharp-scripts
description: Run single-file C# programs as scripts for quick experimentation, prototyping, and concept testing. Use when the user wants to write and execute a small C# program without creating a full project.
---

# C# Scripts

## When to Use

- Testing a C# concept, API, or language feature with a quick one-file program
- Prototyping logic before integrating it into a larger project

## When Not to Use

- The user needs a full project with multiple files or project references
- The user is working inside an existing .NET solution and wants to add code there
- The program is too large or complex for a single file

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| C# code or intent | Yes | The code to run, or a description of what the script should do |

## Workflow

### Step 1: Check the .NET SDK version

Run `dotnet --version` to verify the SDK is installed and note the major version number. File-based apps require .NET 10 or later. If the version is below 10, follow the [fallback for older SDKs](#fallback-for-net-9-and-earlier) instead.

### Step 2: Write the script file

Create a single `.cs` file using top-level statements. Place it outside any existing project directory to avoid conflicts with `.csproj` files.

```csharp
// hello.cs
Console.WriteLine("Hello from a C# script!");

var numbers = new[] { 1, 2, 3, 4, 5 };
Console.WriteLine($"Sum: {numbers.Sum()}");
```

Guidelines:

- Use top-level statements (no `Main` method, class, or namespace boilerplate)
- Place `using` directives at the top of the file
- Place type declarations (classes, records, enums) after all top-level statements

### Step 3: Run the script

```bash
dotnet hello.cs
```

Builds and runs the file automatically. Cached so subsequent runs are fast. Pass arguments after `--`:

```bash
dotnet hello.cs -- arg1 arg2 "multi word arg"
```

### Step 4: Add NuGet packages (if needed)

Use the `#:package` directive at the top of the file to reference NuGet packages. Always specify a version:

```csharp
#:package Humanizer@2.14.1

using Humanizer;

Console.WriteLine("hello world".Titleize());
```

### Step 5: Clean up

Remove the script file when the user is done. To clear cached build artifacts:

```bash
dotnet clean hello.cs
```

## Unix shebang support

On Unix platforms, make a `.cs` file directly executable:

1. Add a shebang as the first line of the file:

    ```csharp
    #!/usr/bin/env dotnet
    Console.WriteLine("I'm executable!");
    ```

2. Set execute permissions:

    ```bash
    chmod +x hello.cs
    ```

3. Run directly:

    ```bash
    ./hello.cs
    ```

Use `LF` line endings (not `CRLF`) when adding a shebang. This directive is ignored on Windows.

## Source-generated JSON

File-based apps enable native AOT by default. Reflection-based APIs like `JsonSerializer.Serialize<T>(value)` fail at runtime under AOT. Use source-generated serialization instead:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

var person = new Person("Alice", 30);
var json = JsonSerializer.Serialize(person, AppJsonContext.Default.Person);
Console.WriteLine(json);

var deserialized = JsonSerializer.Deserialize(json, AppJsonContext.Default.Person);
Console.WriteLine($"Name: {deserialized!.Name}, Age: {deserialized.Age}");

record Person(string Name, int Age);

[JsonSerializable(typeof(Person))]
partial class AppJsonContext : JsonSerializerContext;
```

## Converting to a project

When a script outgrows a single file, convert it to a full project:

```bash
dotnet project convert hello.cs
```

## Fallback for .NET 9 and earlier

If the .NET SDK version is below 10, file-based apps are not available. Use a temporary console project instead:

```bash
mkdir -p /tmp/csharp-script && cd /tmp/csharp-script
dotnet new console -o . --force
```

Replace the generated `Program.cs` with the script content and run with `dotnet run`. Add NuGet packages with `dotnet add package <name>`. Remove the directory when done.

## Validation

- [ ] `dotnet --version` reports 10.0 or later (or fallback path is used)
- [ ] The script compiles without errors (can be checked explicitly with `dotnet build <file>.cs`)
- [ ] `dotnet <file>.cs` produces the expected output
- [ ] Script file and cached artifacts are cleaned up after the session

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| `.cs` file is inside a directory with a `.csproj` | Move the script outside the project directory, or use `dotnet run --file file.cs` |
| `#:package` without a version | Specify a version: `#:package PackageName@1.2.3` or `@*` for latest |
| Reflection-based JSON serialization fails | Use source-generated JSON with `JsonSerializerContext` (see [Source-generated JSON](#source-generated-json)) |
| Unexpected build behavior or version errors | File-based apps inherit `global.json`, `Directory.Build.props`, `Directory.Build.targets`, and `nuget.config` from parent directories. Move the script to an isolated directory if the inherited settings conflict |

## More info

See https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps for a full reference on file-based apps.
