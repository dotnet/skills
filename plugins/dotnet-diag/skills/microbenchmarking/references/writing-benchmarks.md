# Benchmark authoring techniques

This reference covers BenchmarkDotNet features and practices for writing correct benchmark methods. Incorrect benchmarks silently produce misleading results — the techniques here prevent common measurement errors.

## Dead code elimination

The JIT compiler can eliminate code whose result is never used. If a benchmark method returns `void` and doesn't store its result anywhere observable, the JIT may partially or fully optimize away the computation.

**Returning a value from the benchmark method prevents this.** When the method has a non-void return type, the JIT cannot prove the result is unused (the call site in BDN's generated harness is opaque to the JIT), so it must preserve the computation:

```csharp
// Correct — non-void return prevents DCE
[Benchmark]
public int Parse() => int.Parse("12345");

// Wrong — void return, JIT may eliminate the call
[Benchmark]
public void Parse() => int.Parse("12345");
```

For benchmarks that must return `void` (e.g., methods with side effects like `Array.Sort`), DCE is not a concern because the operation itself has observable effects.

For iterating deferred sequences where returning the sequence object doesn't force enumeration, use the `Consumer` class explicitly — see [Deferred execution](#deferred-execution).

## Setup and cleanup

### GlobalSetup / GlobalCleanup

A method marked `[GlobalSetup]` runs once per benchmark case, before all iterations of that case. Use it to allocate buffers, load data, and initialize state that the benchmark reads but does not mutate.

```csharp
private int[] _array;

[GlobalSetup]
public void Setup() => _array = Enumerable.Range(0, 1000).ToArray();

[Benchmark]
public int Sum() => _array.Sum();
```

Initialization logic inside the benchmark method itself is measured — this is almost never what you want.

When a class has multiple benchmarks needing different setup, use the `Target` or `Targets` property:

```csharp
[GlobalSetup(Target = nameof(BenchmarkA))]
public void SetupA() { ... }

[GlobalSetup(Targets = new[] { nameof(BenchmarkB), nameof(BenchmarkC) })]
public void SetupBC() { ... }
```

`[GlobalCleanup]` runs once after all iterations. Use it to dispose resources (files, connections) created during setup.

**Note**: when `[MemoryRandomization]` is enabled, BDN re-runs `[GlobalCleanup]` and `[GlobalSetup]` after every iteration (overriding the normal once-only behavior). See [references/bdn-internals-and-tuning.md](references/bdn-internals-and-tuning.md) for details.

### IterationSetup / IterationCleanup

A method marked `[IterationSetup]` runs before every iteration. Use it only when the benchmark mutates state that must be reset — for example, sorting an array in-place.

**Critical constraint**: when `[IterationSetup]` or `[IterationCleanup]` is used, BDN defaults to `InvocationCount=1` and `UnrollFactor=1` (unless explicitly overridden). This means the entire iteration is a single invocation, so the operation should be long enough (generally hundreds of milliseconds or more) for a single-invocation measurement to be reliable — with only one invocation, OS scheduling jitter and timer resolution become a significant fraction of the measured time.

```csharp
[Params(10_000_000)] // large enough for single-invocation measurement
public int Size { get; set; }

private int[] _data, _scratch;

[GlobalSetup]
public void Setup() => _data = Enumerable.Range(0, Size).Reverse().ToArray();

[IterationSetup]
public void ResetArray() => Array.Copy(_data, _scratch = new int[Size], Size);

[Benchmark]
public void Sort() => Array.Sort(_scratch);
```

**If `[GlobalSetup]` is sufficient, do not use `[IterationSetup]`.** It adds overhead and constrains the measurement.

## OperationsPerInvoke

When part of the benchmark setup cannot be moved to `[GlobalSetup]` — typically because it involves a stack-only type like `Span<T>` — use `OperationsPerInvoke` to amortize the setup cost by performing multiple logical operations per invocation.

```csharp
private byte[] _array = new byte[18];

[Benchmark(OperationsPerInvoke = 16)]
public Span<byte> Slice()
{
    Span<byte> span = new Span<byte>(_array); // setup: can't go in GlobalSetup (stack-only)

    // 16 operations, unrolled without a loop
    span = span.Slice(1); span = span.Slice(1); span = span.Slice(1); span = span.Slice(1);
    span = span.Slice(1); span = span.Slice(1); span = span.Slice(1); span = span.Slice(1);
    span = span.Slice(1); span = span.Slice(1); span = span.Slice(1); span = span.Slice(1);
    span = span.Slice(1); span = span.Slice(1); span = span.Slice(1); span = span.Slice(1);

    return span;
}
```

BDN divides the measured time by `OperationsPerInvoke`, so the reported result reflects a single `Slice` operation with the `Span` creation cost amortized. The operations are manually unrolled (not in a loop) to avoid loop overhead affecting the measurement.

## Avoiding loops

BDN determines how many times to invoke the benchmark method per iteration automatically (the pilot stage). Adding a manual loop inside the benchmark is unnecessary and harmful:

```csharp
// Wrong — manual loop adds loop overhead and hides per-operation variance
[Benchmark]
public int ParseLoop()
{
    int result = 0;
    for (int i = 0; i < 1000; i++)
        result += int.Parse("12345");
    return result;
}

// Correct — BDN handles invocation count
[Benchmark]
public int Parse() => int.Parse("12345");
```

The exception is `OperationsPerInvoke` — when per-invocation setup cannot be avoided, repeating the operation inside the method and declaring the count lets BDN divide the result correctly.

## No side effects

Benchmarks should be idempotent — running the method N times should produce the same performance characteristics as running it once. A benchmark that grows a list on every invocation violates this:

```csharp
// Wrong — list grows with every invocation, later calls are slower
List<int> _numbers = new();

[Benchmark]
public void Add() => _numbers.Add(12345);
```

If the operation inherently mutates state (e.g., `Array.Sort`), use `[IterationSetup]` to reset state between iterations, with input large enough for reliable single-invocation measurement.

## Async benchmarks

BDN natively supports benchmark methods that return `Task`, `Task<T>`, `ValueTask`, or `ValueTask<T>`. BDN awaits the result automatically — there is no special attribute or configuration needed:

```csharp
[Benchmark]
public async Task<int> ReadAsync()
{
    using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
    var buffer = new byte[4096];
    return await stream.ReadAsync(buffer);
}
```

**Async setup/cleanup**: `[GlobalSetup]` and `[GlobalCleanup]` methods can also return `Task` or `ValueTask` — BDN awaits them automatically. `[IterationSetup]` and `[IterationCleanup]` do **not** support async return types; they must be synchronous.

**When NOT to use async**: if the operation being benchmarked is synchronous, do not wrap it in `async`/`await`. The async state machine adds overhead that would be included in the measurement:

```csharp
// Wrong — adds async state machine overhead to a synchronous operation
[Benchmark]
public async Task<int> ParseAsync() => await Task.FromResult(int.Parse("12345"));

// Correct — benchmark the synchronous method directly
[Benchmark]
public int Parse() => int.Parse("12345");
```

## Deferred execution

Returning `IEnumerable<T>`, `IQueryable<T>`, or `Lazy<T>` from a benchmark measures only the creation of the deferred sequence — not its execution. This is almost always a bug:

```csharp
// Wrong — measures only the creation of the LINQ query object, not its execution
[Benchmark]
public IEnumerable<int> WhereQuery() => _numbers.Where(n => n > 50);

// Correct — materialize the result so the query actually executes
[Benchmark]
public List<int> WhereQuery() => _numbers.Where(n => n > 50).ToList();
```

BDN includes a `DeferredExecutionValidator` that detects this mistake and raises a validation error, preventing the benchmark from running. To fix it, either materialize the result (`.ToList()`, `.ToArray()`) or consume it manually using the `.Consume()` extension method:

```csharp
private Consumer _consumer = new();

[Benchmark]
public void WhereQuery() => _numbers.Where(n => n > 50).Consume(_consumer);
```

## Constant folding

The JIT can evaluate expressions at compile time when all inputs are constants or literals. If a benchmark operates on constant inputs, the JIT may fold the computation into a precomputed result, and the benchmark measures nothing:

```csharp
// Wrong — JIT can compute Math.Sqrt(144.0) at compile time
[Benchmark]
public double Sqrt() => Math.Sqrt(144.0);

// Correct — field value is not a compile-time constant
private double _value = 144.0;

[Benchmark]
public double Sqrt() => Math.Sqrt(_value);
```

Store inputs in fields, `[Params]`, or `[Arguments]` — never as literals or `const` values in the benchmark method.

## Randomness and reproducibility

When benchmarks use generated input data, use a fixed seed so results are comparable across runs:

```csharp
[GlobalSetup]
public void Setup()
{
    var rng = new Random(42); // fixed seed
    _data = new byte[Size];
    rng.NextBytes(_data);
}
```

## Benchmark class constraints

BDN generates source code for a derived type from the benchmark class, which is compiled as part of its generated project. This imposes constraints:
- The class must be `public`
- The class must not be `sealed`, `static`, or `abstract`
- The class must be a `class`, not a `struct` or `ref struct`
- Benchmark methods must be `public` and instance (not `static`)

## Parameterization summary

| Feature | Target | Use when |
|---------|--------|----------|
| `[Params]` | Field/Property | Values needed in setup; applies to all benchmarks in the class |
| `[ParamsSource]` | Field/Property | Dynamic or complex values needed in setup |
| `[ParamsAllValues]` | Field/Property | Enum or bool — test every possible value |
| `[Arguments]` | Method | Inline values for a specific benchmark method |
| `[ArgumentsSource]` | Method | Dynamic or complex values for a specific benchmark method |
| `[GenericTypeArguments]` | Class | Vary type parameters (e.g., value type vs reference type) |

`[Params]` applies to all benchmarks in the class. If different benchmarks need different parameter ranges, split them into separate classes.

`[Arguments]` and `[ArgumentsSource]` are method-specific and their initialization time is excluded from the measurement. `[ArgumentsSource]` methods return `IEnumerable<T>` for single-parameter benchmarks, or `IEnumerable<object[]>` for multi-parameter benchmarks (each array is one set of arguments). `[ParamsSource]` methods return `IEnumerable<T>` (each element is a single value assigned to the field/property).

```csharp
// Single-parameter: IEnumerable<T>
[Benchmark]
[ArgumentsSource(nameof(Inputs))]
public int Parse(string text) => int.Parse(text);

public IEnumerable<string> Inputs()
{
    yield return "12345";
    yield return "999999999";
    yield return "-42";
}

// Multi-parameter: IEnumerable<object[]>
[Benchmark]
[ArgumentsSource(nameof(Inputs))]
public bool TryParse(string text, NumberStyles style) => int.TryParse(text, style, null, out _);

public IEnumerable<object[]> Inputs()
{
    yield return new object[] { "12345", NumberStyles.Integer };
    yield return new object[] { "0xFF", NumberStyles.HexNumber };
}
```

`[ParamsSource]` works the same way but targets a field/property and applies to all benchmarks in the class:

```csharp
[ParamsSource(nameof(Sizes))]
public int N;

public IEnumerable<int> Sizes() => new[] { 100, 1_000, 10_000 };
```

`[GenericTypeArguments]` varies type parameters on a generic benchmark class. Each attribute creates a separate closed generic type that BDN runs independently:

```csharp
[GenericTypeArguments(typeof(int))]
[GenericTypeArguments(typeof(string))]
public class CollectionBenchmark<T>
{
    private List<T> _list;

    [GlobalSetup]
    public void Setup() => _list = new List<T>(Enumerable.Repeat(default(T)!, 1000));

    [Benchmark]
    public bool Contains() => _list.Contains(default!);
}
```
