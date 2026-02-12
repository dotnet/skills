---
name: source-generator-authoring
description: Guides scaffolding, implementation, and review of Roslyn source generators and analyzers. Use when creating a new IIncrementalGenerator, reviewing generator code for IDE performance, writing GeneratorDriver tests, or diagnosing incremental compilation issues.
---

# Source Generator Authoring

This skill helps you create, review, and test Roslyn incremental source generators and analyzers that follow Roslyn team best practices for correctness, IDE performance, and incremental compilation.

## When to Use

- Creating a new `IIncrementalGenerator` from scratch
- Reviewing existing generator code for performance or correctness issues
- Writing tests for a source generator using `GeneratorDriver`
- Diagnosing why a generator re-runs unnecessarily or degrades IDE responsiveness
- Adding diagnostic reporting to a generator or analyzer
- Migrating from `ISourceGenerator` to `IIncrementalGenerator`

## When Not to Use

- Writing a standalone Roslyn analyzer without source generation (use analyzer-only patterns)
- Building a full Roslyn code fix provider (different lifecycle and API surface)
- Creating compile-time metaprogramming that does not use the Roslyn generator pipeline

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Generator purpose | Yes | What code the generator will emit and what triggers it (attributes, interfaces, naming conventions) |
| Target framework | Yes | The TFM of the consuming project (affects available Roslyn APIs) |
| Trigger mechanism | Yes | How the generator discovers candidates: attribute markers, interface implementations, partial declarations, or assembly-level attributes |
| Existing generator code | If reviewing | Source files to review for performance or correctness |

## Workflow

### Step 1: Scaffold the generator project

Create a `netstandard2.0` class library. Source generators must target `netstandard2.0` regardless of the consuming project's TFM.

Required project file settings:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.3.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.4" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

Key points:
- `EnforceExtendedAnalyzerRules` catches common mistakes at build time
- `IsRoslynComponent` enables Roslyn-specific build behaviors
- Pin `Microsoft.CodeAnalysis.CSharp` to the lowest version you need to support
- Mark all Roslyn packages `PrivateAssets="all"` so the host provides them at runtime

### Step 2: Implement the incremental generator

Always use `IIncrementalGenerator`. The older `ISourceGenerator` interface re-runs on every keystroke and must not be used for new generators.

```csharp
[Generator]
public sealed class MyGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Create a syntax provider that filters cheaply in the predicate
        var declarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "MyNamespace.MyMarkerAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => ExtractModel(ctx, ct))
            .Where(static m => m is not null);

        // 2. Combine with any additional inputs if needed
        // 3. Register the output
        context.RegisterSourceOutput(declarations, static (spc, model) =>
        {
            // Emit source here
        });
    }
}
```

Critical rules for the pipeline:

1. **Use `ForAttributeWithMetadataName`** when triggered by attributes. It is the fastest filter because the compiler pre-indexes attribute names. Fall back to `CreateSyntaxProvider` only when attributes are not the trigger.
2. **Keep the predicate cheap.** The predicate runs on every syntax node change. Do only fast type checks (e.g., `node is ClassDeclarationSyntax`). Never access the semantic model in the predicate.
3. **Extract a plain data model in the transform.** The transform should return a simple record or value type holding only the data needed for code emission. Never store `ISymbol`, `SyntaxNode`, `Compilation`, `SemanticModel`, or any Roslyn object in the model — they hold the entire compilation in memory and break caching.
4. **Mark lambdas `static`.** This prevents accidental closure captures that defeat caching and cause allocations on every IDE keystroke.

### Step 3: Design the data model for caching

The pipeline caches results between transform and output steps. Caching works only when the model type has correct value equality.

```csharp
// Use a record for automatic value equality
internal sealed record MyModel(
    string Namespace,
    string TypeName,
    EquatableArray<string> MemberNames);
```

Rules for the model type:

1. **Use records or manually implement `IEquatable<T>`.** The pipeline calls `Equals` to decide whether to re-run downstream steps. Incorrect equality means the generator either re-runs every time (wasting CPU) or never re-runs (producing stale output).
2. **Never store Roslyn API objects.** `ISymbol`, `ITypeSymbol`, `SyntaxNode`, `Location`, `Compilation`, and `SemanticModel` do not have value equality and hold references to the entire compilation tree. Extract primitive data (strings, bools, enums) instead.
3. **Wrap collections in an equatable wrapper.** Arrays and lists do not implement value equality. Use `ImmutableArray<T>` wrapped in an `EquatableArray<T>` helper or a custom wrapper that implements `IEquatable` with `SequenceEqual`.
4. **Keep the model small.** Only include data needed for code emission. Smaller models mean faster equality checks and less GC pressure in the IDE.

### Step 4: Implement the EquatableArray helper

If the model contains collections, provide this helper:

```csharp
internal readonly struct EquatableArray<T>(ImmutableArray<T> array)
    : IEquatable<EquatableArray<T>>
    where T : IEquatable<T>
{
    public ImmutableArray<T> AsImmutableArray() => array;

    public bool Equals(EquatableArray<T> other)
        => array.AsSpan().SequenceEqual(other.array.AsSpan());

    public override bool Equals(object? obj)
        => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in array)
            hash.Add(item);
        return hash.ToHashCode();
    }
}
```

### Step 5: Emit generated source

Use string building or raw string literals. Avoid `SyntaxFactory` in generators — it is allocation-heavy and designed for refactoring tools, not code generation.

```csharp
context.RegisterSourceOutput(declarations, static (spc, model) =>
{
    var source = $$"""
        // <auto-generated/>
        #nullable enable

        namespace {{model.Namespace}};

        partial class {{model.TypeName}}
        {
            // generated members
        }
        """;

    spc.AddSource($"{model.TypeName}.g.cs", SourceText.From(source, Encoding.UTF8));
});
```

Rules for emission:

1. **Add the `// <auto-generated/>` header.** This tells analyzers and IDE features to skip the file.
2. **Use a deterministic hint name.** The hint name (first argument to `AddSource`) must be unique per generator output and stable across runs. Use the type name or a hash. Never use `Guid.NewGuid()`.
3. **Use `SourceText.From` with `Encoding.UTF8`.** This ensures correct encoding and avoids BOM issues.
4. **Emit `#nullable enable`** so the generated code participates in nullable analysis.

### Step 6: Report diagnostics correctly

Register diagnostics with `RegisterSourceOutput` or in the transform step via `SourceProductionContext.ReportDiagnostic`. Define descriptors as `static readonly` fields.

```csharp
private static readonly DiagnosticDescriptor MissingPartialKeyword = new(
    id: "MYGEN001",
    title: "Type must be partial",
    messageFormat: "The type '{0}' must be declared partial to use [MyMarker]",
    category: "MyGenerator",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true);
```

Rules:

1. **Use a unique, stable diagnostic ID** with a short prefix and numeric suffix (e.g., `MYGEN001`).
2. **Do not throw exceptions from the generator.** Report a diagnostic and return gracefully. Unhandled exceptions crash the IDE analyzer process.
3. **Store `Location` data as file path, line, and column in the model** if you need to report diagnostics in the output step. Do not store `Location` objects directly.

### Step 7: Write tests using GeneratorDriver

Create a test project referencing `Microsoft.CodeAnalysis.CSharp` and your generator assembly. Use the `CSharpGeneratorDriver` API.

```csharp
[Fact]
public void Generator_Produces_Expected_Output()
{
    // 1. Create a compilation from source text
    var source = """
        using MyNamespace;

        namespace TestNs;

        [MyMarker]
        public partial class Foo { }
        """;

    var compilation = CSharpCompilation.Create("TestAssembly",
        [CSharpSyntaxTree.ParseText(source)],
        [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    // 2. Create and run the driver
    var generator = new MyGenerator();
    GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
    driver = driver.RunGeneratorsAndUpdateCompilation(
        compilation, out var outputCompilation, out var diagnostics);

    // 3. Assert no diagnostics
    Assert.Empty(diagnostics);
    Assert.Empty(outputCompilation.GetDiagnostics()
        .Where(d => d.Severity == DiagnosticSeverity.Error));

    // 4. Verify generated source
    var runResult = driver.GetRunResult();
    var generatedSource = runResult.GeneratedTrees.Single();
    var text = generatedSource.GetText().ToString();
    Assert.Contains("partial class Foo", text);
}
```

Test the incremental caching behavior:

```csharp
[Fact]
public void Generator_Does_Not_Rerun_On_Unrelated_Change()
{
    // Run once
    var source = """...""";
    var compilation = CreateCompilation(source);
    GeneratorDriver driver = CSharpGeneratorDriver.Create(new MyGenerator());
    driver = driver.RunGeneratorsAndUpdateCompilation(
        compilation, out _, out _);

    // Modify an unrelated file and re-run with the SAME driver instance
    var newTree = CSharpSyntaxTree.ParseText("class Unrelated { }");
    var updatedCompilation = compilation.AddSyntaxTrees(newTree);
    driver = driver.RunGeneratorsAndUpdateCompilation(
        updatedCompilation, out _, out _);

    // The generator output step should not have re-run
    var result = driver.GetRunResult().Results.Single();
    Assert.All(result.TrackedOutputSteps.SelectMany(s => s.Value),
        step => Assert.Equal(
            IncrementalStepRunReason.Cached,
            step.Outputs.Single().Reason));
}
```

Key testing points:

1. **Reuse the same `GeneratorDriver` instance** across runs to test incremental caching. Creating a new driver resets the cache.
2. **Assert `IncrementalStepRunReason.Cached`** for steps that should not re-run.
3. **Test error cases** by providing source that triggers diagnostics and verifying the correct diagnostic IDs appear.
4. **Include metadata references** for any types the source uses. Missing references cause silent semantic model failures.

### Step 8: Review for IDE performance

After the generator compiles, audit it against this checklist. Every violation can degrade the IDE experience for all users of the consuming project.

1. **No Roslyn objects stored in the model.** Search for fields or properties of type `ISymbol`, `ITypeSymbol`, `SyntaxNode`, `Compilation`, `SemanticModel`, or `Location` in any type returned from a transform.
2. **No semantic model access in the predicate.** The predicate callback in `CreateSyntaxProvider` must use only the `SyntaxNode` parameter.
3. **All lambdas in the pipeline are `static`.** Non-static lambdas capture `this` or local variables, causing allocations per invocation.
4. **Model types implement value equality.** Records do this automatically. For classes or structs, verify `IEquatable<T>` is implemented and tested.
5. **No `SyntaxFactory` usage in emission.** Use string interpolation or `StringBuilder` instead.
6. **No file I/O or network calls.** Generators run in the compiler process; blocking calls freeze the IDE.
7. **No `Thread` or `Task` usage.** Use only the `CancellationToken` passed to the transform. The pipeline manages parallelism.
8. **`CancellationToken` is checked in long loops.** Call `ct.ThrowIfCancellationRequested()` inside any loop that iterates over members or syntax nodes.

## Validation

- [ ] Generator project targets `netstandard2.0` and has `EnforceExtendedAnalyzerRules` set to `true`
- [ ] Generator class implements `IIncrementalGenerator` (not `ISourceGenerator`)
- [ ] All transform and output lambdas are `static`
- [ ] Data model uses records or implements `IEquatable<T>` with value semantics
- [ ] No `ISymbol`, `SyntaxNode`, `Compilation`, `SemanticModel`, or `Location` stored in the model
- [ ] Collections in the model are wrapped for value equality
- [ ] Tests use `CSharpGeneratorDriver` and reuse the driver instance for incremental tests
- [ ] At least one test asserts `IncrementalStepRunReason.Cached` for unrelated edits
- [ ] Diagnostics use `static readonly DiagnosticDescriptor` with stable IDs
- [ ] Generated source includes `// <auto-generated/>` header
- [ ] Hint names are deterministic and unique per output
- [ ] No exceptions thrown from generator code paths — errors reported as diagnostics

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Using `ISourceGenerator` instead of `IIncrementalGenerator` | Always use `IIncrementalGenerator`. The older interface re-runs on every keystroke. |
| Storing `ISymbol` or `SyntaxNode` in the model | Extract primitives (strings, bools) in the transform. Roslyn objects defeat caching and leak memory. |
| Non-static lambdas in the pipeline | Add the `static` keyword to all lambdas passed to `ForAttributeWithMetadataName`, `Select`, `Where`, and `RegisterSourceOutput`. |
| Model type without value equality | Use a `record` or implement `IEquatable<T>`. Without it the pipeline re-runs every step on every keystroke. |
| Using `CreateSyntaxProvider` when an attribute trigger exists | Use `ForAttributeWithMetadataName` — it uses a compiler-maintained index and is significantly faster. |
| Accessing `SemanticModel` in the predicate | Move semantic work to the transform callback. The predicate must be syntax-only. |
| Using `SyntaxFactory` to build output | Use string interpolation or `StringBuilder`. `SyntaxFactory` causes heavy allocations unsuitable for generators. |
| Throwing exceptions on invalid input | Report a `Diagnostic` and return. Exceptions crash the analyzer process and disable all generators in the project. |
| Generating non-deterministic hint names | Use stable names based on the type or input. Random names cause unnecessary file churn and break caching. |
| Missing metadata references in tests | Add references for `System.Runtime` and any attribute assemblies. Missing references cause the semantic model to return null symbols silently. |
