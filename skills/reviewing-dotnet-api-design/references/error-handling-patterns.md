# Error Handling Patterns Reference

## Standard Exception Types

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

**Never throw directly:**
- `Exception`, `SystemException`, `ApplicationException`
- `NullReferenceException` or `IndexOutOfRangeException` (runtime errors only)

## Argument Validation Patterns

### Modern Pattern (.NET 6+)

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

Note: Use `value` as the parameter name in property setter exceptions.

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

Provide both throwing and non-throwing variants for operations that commonly fail:

```csharp
// Throwing variant
public static int Parse(string s);
// Non-throwing variant
public static bool TryParse(string s, out int result);
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

```csharp
"Stream does not support reading."
"Non-negative number required. (Parameter 'count')"
"Collection was modified; enumeration operation may not execute."
"Index was out of range. Must be non-negative and less than the size of the collection."
```

**Requirements:**
- Complete sentences with proper punctuation
- State the problem clearly
- Include relevant values when possible
- Don't expose internal implementation details

## Async Exception Patterns

Validate arguments synchronously (before the first `await`) so callers get immediate feedback:

```csharp
public Task<string> ReadFileAsync(string path, CancellationToken cancellationToken)
{
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

Helper methods keep call sites small enough for JIT inlining:

```csharp
private static void ThrowInvalidOperation()
    => throw new InvalidOperationException("Enumeration already finished.");

public bool MoveNext()
{
    if (_index >= _count)
    {
        ThrowInvalidOperation();
    }
    // ...
}
```

## Methods That Should Not Throw

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
