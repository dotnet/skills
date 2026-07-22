---
name: csharp-scripts
description: "Run file-based C# apps with the .NET CLI when the user explicitly wants C#/.NET code without creating a project. Use for C# language/API experiments, one-file C# apps, small multi-file C# apps composed with `#:include`/`#:exclude`, or C# file-based apps linked with `#:ref`. Do not use for language-agnostic throwaway scripts, generic computations, Python/PowerShell-style automation, full projects, or existing app integration."
license: MIT
---

# File-Based C# Apps

## When to Use

- Testing a C# concept, API, or language feature with a quick file-based app
- Prototyping logic before integrating it into a larger project
- Building a small utility from one entry-point file and a few helper `.cs` files

## When Not to Use

- The user asks for a language-agnostic quick script, throwaway computation, or shell/Python/PowerShell-style automation
- The user needs a full project, solution integration, or project references in an existing app
- The user is working inside an existing .NET solution and wants to add code there
- The app is large enough that project structure, build customization, tests, or publish configuration should live in a `.csproj`

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| C# code or intent | Yes | The code to run, or a description of what the file-based app should do |

## Workflow

### Step 1: Check the .NET SDK version

Run `dotnet --version` to verify the SDK is installed and note the full version, including the feature band. File-based apps require .NET 10 or later. `#:include`, `#:exclude`, and transitive directive processing require SDK 10.0.300 or later; SDK 10.0.100/10.0.200 builds can run single-file apps but do not support those multi-file directives. If the version is below 10, follow the [fallback for older SDKs](#fallback-for-net-9-and-earlier) instead.

### Step 2: Write the app file

Create an entry-point `.cs` file using top-level statements. Place it outside any existing project directory to avoid conflicts with `.csproj` files.

```csharp
#!/usr/bin/env dotnet
// hello.cs
Console.WriteLine("Hello from a file-based app!");

var numbers = new[] { 1, 2, 3, 4, 5 };
Console.WriteLine($"Sum: {numbers.Sum()}");
```

Guidelines:

- Use top-level statements (no `Main` method, class, or namespace boilerplate)
- Place `using` directives at the top of the file (after the `#!` line and any `#:` directives if present)
- Place type declarations (classes, records, enums) after all top-level statements

### Step 3: Run the app

```bash
dotnet hello.cs
```

Builds and runs the file automatically. Cached so subsequent runs are fast. Pass arguments after `--`:

```bash
dotnet hello.cs -- arg1 arg2 "multi word arg"
```

### Step 4: Add directives (if needed)

Place directives at the top of the file (immediately after an optional shebang line), before any `using` directives or other C# code. All directives start with `#:`.

#### `#:package` — NuGet package references

Specify a version unless the app intentionally uses central package management. Use `@*` when the latest available package is acceptable (or `@*-*` for pre-release):

```csharp
#:package Humanizer@2.14.1

using Humanizer;

Console.WriteLine("hello world".Titleize());
```

#### `#:property` — MSBuild properties

Set any MSBuild property inline. Syntax: `#:property PropertyName=Value`

```csharp
#:property AllowUnsafeBlocks=true
#:property PublishAot=false
#:property NoWarn=CS0162
```

MSBuild expressions and property functions are supported:

```csharp
#:property LogLevel=$([MSBuild]::ValueOrDefault('$(LOG_LEVEL)', 'Information'))
```

Common properties:

| Property | Purpose |
|----------|---------|
| `AllowUnsafeBlocks=true` | Enable `unsafe` code |
| `PublishAot=false` | Disable native AOT (enabled by default) |
| `NoWarn=CS0162;CS0219` | Suppress specific warnings |
| `LangVersion=preview` | Enable preview language features |
| `InvariantGlobalization=false` | Enable culture-specific globalization |

#### `#:project` — Project references

Reference another project by relative path:

```csharp
#:project ../MyLibrary/MyLibrary.csproj
```

#### `#:ref` — File-based app references

Reference another `.cs` file as a separate file-based app project when it should compile into a separate assembly instead of being included in the same compilation. Use `#:include` for ordinary helper files that should share the same assembly as the entry point; use `#:ref` when you want project-reference-like boundaries.

```csharp
#:property ExperimentalFileBasedProgramEnableRefDirective=true
#:ref ../Shared/Formatter.cs

Console.WriteLine(Formatter.Title("hello world"));
```

Guidelines:

- The referenced file is compiled as its own virtual project and added as a project reference.
- If the referenced file is a library without top-level statements, put `#:property OutputType=Library` in that referenced file.
- Members that must be consumed by the referencing app should be public; internal members are not visible across the assembly boundary.
- `#:ref` is transitive: a referenced file can contain its own `#:ref` and other `#:` directives.
- Relative paths are resolved relative to the file containing the directive.
- Some SDK builds require `#:property ExperimentalFileBasedProgramEnableRefDirective=true`; remove that property if the SDK accepts `#:ref` without it.

#### `#:sdk` — SDK selection

Override the default SDK (`Microsoft.NET.Sdk`):

```csharp
#:sdk Microsoft.NET.Sdk.Web
```

#### `#:include` and `#:exclude` — Multi-file apps

In .NET SDK 10.0.300 and later, file-based apps can include additional files in the same virtual project. Check the full `dotnet --version` output before using these directives; a 10.0.100 or 10.0.200 SDK is still .NET 10 but does not support them. Use `#:include` for helper source files and supported assets, and `#:exclude` to remove files from an include pattern or default item set.

```csharp
#!/usr/bin/env dotnet
#:include Helpers.cs
#:include Models/*.cs
#:exclude Models/Generated/*.cs

Console.WriteLine(Formatter.Title("hello world"));
```

Guidelines:

- Treat the file passed to `dotnet` as the entry point; put top-level statements there.
- Put declarations such as classes, records, and enums in included `.cs` files.
- Prefer explicit globs such as `Helpers.cs` or `Models/*.cs` over broad recursive globs.
- Paths are resolved relative to the file containing the directive.
- Include directives from non-entry-point C# files are processed too, so a helper file can declare its own `#:package`, `#:property`, `#:sdk`, `#:project`, `#:ref`, `#:include`, or `#:exclude` directives.
- Avoid duplicate directives across included files unless the directive kind explicitly supports duplicates; duplicate `#:package`, `#:property`, `#:sdk`, `#:include`, and `#:exclude` entries can fail.
- When an app uses `#:include`, add a shebang (`#!/usr/bin/env dotnet`) to the entry-point file on Unix-like systems to make the entry point clear to tools. Use `LF` line endings and no BOM for shebang files.

Example layout:

```text
scratch/
    hello.cs
    Helpers.cs
    Models/
        Person.cs
```

```csharp
#!/usr/bin/env dotnet
// hello.cs
#:include Helpers.cs
#:include Models/*.cs

var person = new Person("Ada");
Console.WriteLine(Formatter.Title(person.Name));
```

```csharp
// Helpers.cs
static class Formatter
{
    public static string Title(string value) => value.ToUpperInvariant();
}
```

```csharp
// Models/Person.cs
record Person(string Name);
```

### Step 5: Clean up

Remove the app files when the user is done. To clear cached build artifacts:

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

When a file-based app outgrows this workflow, convert it to a full project:

```bash
dotnet project convert hello.cs
```

## Fallback for .NET 9 and earlier

If the .NET SDK version is below 10, file-based apps are not available. Use a temporary console project instead:

```bash
mkdir -p /tmp/csharp-file-based-app && cd /tmp/csharp-file-based-app
dotnet new console -o . --force
```

Replace the generated `Program.cs` with the app content and run with `dotnet run`. Add NuGet packages with `dotnet add package <name>`. Remove the directory when done.

## Validation

- [ ] `dotnet --version` reports 10.0 or later (or fallback path is used)
- [ ] If the app uses `#:include`, `#:exclude`, or transitive directives from included files, `dotnet --version` reports SDK 10.0.300 or later
- [ ] The app compiles without errors (can be checked explicitly with `dotnet build <file>.cs`)
- [ ] `dotnet <file>.cs` produces the expected output
- [ ] Multi-file apps include every required helper file and exclude unintended matches
- [ ] App files and cached artifacts are cleaned up after the session

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| `.cs` file is inside a directory with a `.csproj` | Move the app outside the project directory, or use `dotnet run --file file.cs` |
| `#:package` without a version | Specify a version: `#:package PackageName@1.2.3` or `@*` for latest |
| `#:property` with wrong syntax | Use `PropertyName=Value` with no spaces around `=` and no quotes: `#:property AllowUnsafeBlocks=true` |
| Directives placed after C# code | All `#:` directives must appear immediately after an optional shebang line (if present) and before any `using` directives or other C# statements |
| Helper file is not compiled | Add `#:include Helper.cs` or an appropriate glob to the entry-point file |
| Shared file needs an assembly boundary | Use `#:ref Shared.cs` instead of `#:include Shared.cs`, and set `#:property OutputType=Library` in the referenced file if it has no entry point |
| Broad include pulls in unrelated files | Prefer narrow include patterns and use `#:exclude` for generated, backup, or experimental files |
| Duplicate directives in included files | Keep package, property, SDK, include, and exclude directives unique across the entry point and included C# files |
| Reflection-based JSON serialization fails | Use source-generated JSON with `JsonSerializerContext` (see [Source-generated JSON](#source-generated-json)) |
| Unexpected build behavior or version errors | File-based apps inherit `global.json`, `Directory.Build.props`, `Directory.Build.targets`, and `nuget.config` from parent directories. Move the app to an isolated directory if the inherited settings conflict |

## More info

See https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps for a full reference on file-based apps.
