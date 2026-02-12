# Error Handling Patterns Reference

Established C# error handling conventions.

## Standard Exception Types

Use a consistent set of exception types. Custom exception types should be rare.

| Exception Type | When It Is Thrown | Example |
|---------------|----------------------|---------|
| `ArgumentNullException` | A null argument was passed | `File.Open(null, ...)` |
| `ArgumentOutOfRangeException` | A value is outside the valid range | `new List<int>(-1)` |
| `ArgumentException` | General argument validation failure | `new Uri("not a valid uri")` |
| `InvalidOperationException` | Object state doesn't support the call | `enumerator.Current` before `MoveNext` |
| `NotSupportedException` | Operation is inherently unsupported | `readOnlyStream.Write(...)` |
| `ObjectDisposedException` | Object has been disposed | Using a disposed `HttpClient` |
| `OperationCanceledException` | Operation was canceled | `cancellationToken.ThrowIfCancellationRequested()` |
| `FormatException` | String is not in the expected format | `int.Parse("abc")` |
| `IOException` | I/O operation failed | File not found, disk full |
| `UnauthorizedAccessException` | Caller lacks permission | Accessing a protected file |
| `KeyNotFoundException` | Key not found in dictionary | `dict["missing_key"]` |
| `IndexOutOfRangeException` | Array/span index invalid | Runtime-thrown, not user code |
| `NullReferenceException` | Null dereference | Runtime-thrown, not user code |

**What to avoid:**
- Throws `Exception` directly
- Throws `SystemException` directly
- Throws `ApplicationException` (legacy, unused)
- Throws `NullReferenceException` or `IndexOutOfRangeException` from library code (these are runtime errors)

## Argument Validation Patterns

### Modern Pattern (.NET 6+)
Use static `ThrowIf` methods for argument validation:

```csharp
public void SetName(string name)
{
    ArgumentNullException.ThrowIfNull(name);
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    _name = name;
}

public void SetCount(int count)
{
    ArgumentOutOfRangeException.ThrowIfNegative(count);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MaxCount);
    _count = count;
}

public void DoWork()
{
    ObjectDisposedException.ThrowIf(_disposed, this);
    // ...
}
```

### Property Setter Validation
```csharp
public string Name
{
    get => _name;
    set
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        _name = value;
    }
}
```

Note: Use `value` as the parameter name in property setter exceptions (it's the implicit parameter name).

### Legacy Pattern (pre-.NET 6)
```csharp
public void SetName(string name)
{
    if (name is null)
        throw new ArgumentNullException(nameof(name));
    if (string.IsNullOrWhiteSpace(name))
        throw new ArgumentException("Name cannot be empty.", nameof(name));
    _name = name;
}
```

## The Try-Parse Pattern

The convention is to provide both throwing and non-throwing variants for operations that commonly fail:

```csharp
// Throwing variant — for when failure is exceptional
public static int Parse(string s);
public static DateTime Parse(string s);
public static IPAddress Parse(string ipString);

// Non-throwing variant — for when failure is expected
public static bool TryParse(string s, out int result);
public static bool TryParse(string s, out DateTime result);
public static bool TryParse(string ipString, out IPAddress? address);
```

**Modern Try-Parse pattern (.NET 7+):**
```csharp
// IParsable<T> interface standardizes this pattern
public interface IParsable<TSelf> where TSelf : IParsable<TSelf>
{
    static abstract TSelf Parse(string s, IFormatProvider? provider);
    static abstract bool TryParse(string? s, IFormatProvider? provider, out TSelf result);
}
```

### When to provide Try-Parse:
- Parsing user input (strings to values)
- Dictionary lookups (`TryGetValue`)
- Any operation where failure is a common, non-exceptional scenario

## Exception Messages

Established convention: exception messages describe what went wrong and often hint at what to do:

```csharp
// Good messages
"Stream does not support reading."
"Non-negative number required. (Parameter 'count')"
"Collection was modified; enumeration operation may not execute."
"Index was out of range. Must be non-negative and less than the size of the collection."
```

**Characteristics of good exception messages:**
- Complete sentences with proper punctuation
- State the problem clearly
- Include relevant values when possible
- Don't expose internal implementation details

## Async Exception Patterns

Validate arguments synchronously (before the first `await`) so callers get immediate feedback:

```csharp
public Task<string> ReadFileAsync(string path, CancellationToken cancellationToken)
{
    // Validate BEFORE async work — throws immediately
    ArgumentNullException.ThrowIfNull(path);
    return ReadFileCoreAsync(path, cancellationToken);
}

private async Task<string> ReadFileCoreAsync(string path, CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();
    return await File.ReadAllTextAsync(path, ct);
}
```

## Exception Builder Pattern

Use helper methods to throw exceptions, keeping call sites small enough for JIT inlining:

```csharp
// Common pattern for hot paths
private static void ThrowInvalidOperation()
    => throw new InvalidOperationException("Enumeration already finished.");

public bool MoveNext()
{
    if (_index >= _count)
    {
        ThrowInvalidOperation();  // Keeps MoveNext small for inlining
    }
    // ...
}
```

## Methods That Should Not Throw

Established convention: these methods avoid throwing exceptions:

| Method | Why |
|--------|-----|
| `Equals(object)` | Used in comparisons, hash tables — must be safe |
| `GetHashCode()` | Used in dictionaries, hash sets — must be safe |
| `ToString()` | Used in debugging, logging — must be safe |
| `Dispose()` | Cleanup should never fail |
| `==` / `!=` operators | Must behave like `Equals` |
| Static constructors | Exception causes `TypeInitializationException`, unrecoverable |

## Error Handling Checklist

- [ ] Standard exception types used (not `Exception` or `SystemException`)
- [ ] `paramName` set on all `ArgumentException` subtypes
- [ ] `ThrowIfNull`, `ThrowIfNegative`, etc. used where available (.NET 6+)
- [ ] Exception messages are clear and describe the problem
- [ ] Try-Parse pattern provided for commonly-failing operations
- [ ] Arguments validated synchronously in async methods
- [ ] `Equals`, `GetHashCode`, `ToString` do not throw
- [ ] Exception builder methods used in hot paths (for JIT inlining)
- [ ] `CancellationToken.ThrowIfCancellationRequested()` used for cancellation
