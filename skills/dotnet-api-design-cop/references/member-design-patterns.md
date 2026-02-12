# Member Design Patterns Reference

Established C# member design conventions.

## Properties vs Methods

There is a clear line between properties (state access) and methods (operations).

### Use Properties When:
- Access is cheap (field-like)
- Calling twice returns the same value
- No observable side effects
- Represents a logical attribute of the type

```csharp
// System.IO.Stream — properties for state
public abstract long Length { get; }
public abstract long Position { get; set; }
public abstract bool CanRead { get; }
public abstract bool CanSeek { get; }

// System.Collections.Generic.List<T>
public int Count { get; }
public int Capacity { get; set; }
```

### Use Methods When:
- The operation is a conversion (`ToString()`, `ToArray()`)
- The call is expensive or has side effects
- Different results each time (`DateTime.Now` is a well-known exception)
- Returns a new object or array

```csharp
// Conversions — always methods
public override string ToString();
public T[] ToArray();
public List<T> ToList();

// Operations with side effects — always methods
public int Read(byte[] buffer, int offset, int count);
public void Write(byte[] buffer, int offset, int count);

// Expensive operations — always methods
public DataTable GetSchemaTable();
public byte[] ComputeHash(byte[] buffer);
```

## Method Overloading Patterns

Established convention: overloads form a progressive series from simplest to most complete.

### StringBuilder.Append Pattern
```csharp
// Simplest → most complete, all consistent
public StringBuilder Append(string value);
public StringBuilder Append(string value, int startIndex, int count);
public StringBuilder Append(char value);
public StringBuilder Append(char value, int repeatCount);
```

### Stream.Read Pattern
```csharp
// Modern .NET adds Span overloads alongside array overloads
public abstract int Read(byte[] buffer, int offset, int count);
public virtual int Read(Span<byte> buffer);
```

### Console.WriteLine Pattern
```csharp
// Many overloads, all following the same naming
public static void WriteLine();
public static void WriteLine(string value);
public static void WriteLine(string format, object arg0);
public static void WriteLine(string format, object arg0, object arg1);
public static void WriteLine(string format, params object[] arg);
```

**Key patterns:**
1. Parameter order is consistent across all overloads
2. Simpler overloads delegate to the most complete one
3. Parameter names are identical across overloads
4. `CancellationToken` is always the last parameter
5. `params` array overload is the most flexible variant

## Constructor Patterns

### Simple Instantiation
The convention supports creating instances with minimal ceremony:

```csharp
// Default constructor — ready to use
var sb = new StringBuilder();
var list = new List<string>();

// Parameterized — for required values
var uri = new Uri("https://example.com");
var fs = new FileStream(path, FileMode.Open);

// Common pattern: overloads from minimal to full
public StringBuilder();
public StringBuilder(string value);
public StringBuilder(int capacity);
public StringBuilder(string value, int capacity);
```

### Argument Validation in Constructors
```csharp
public FileStream(string path, FileMode mode)
{
    ArgumentNullException.ThrowIfNull(path);
    ArgumentException.ThrowIfNullOrEmpty(path);
    // ...
}
```

## Event Patterns

Established event design conventions (as seen in `FileSystemWatcher`, `ObservableCollection<T>`, etc.):

### Standard Pattern
```csharp
// 1. Define EventArgs if needed
public class FileChangedEventArgs : EventArgs
{
    public string FileName { get; }
    public WatcherChangeTypes ChangeType { get; }

    public FileChangedEventArgs(string fileName, WatcherChangeTypes changeType)
    {
        FileName = fileName;
        ChangeType = changeType;
    }
}

// 2. Declare event using EventHandler<T>
public event EventHandler<FileChangedEventArgs> FileChanged;

// 3. Raise through protected virtual method
protected virtual void OnFileChanged(FileChangedEventArgs e)
{
    FileChanged?.Invoke(this, e);
}
```

### Established Conventions:
- `EventHandler<TEventArgs>` is the standard delegate type
- Raising method is named `On<EventName>`
- Raising method is `protected virtual` for extensibility
- `EventArgs.Empty` used when no data is needed
- EventArgs properties are typically read-only

## Operator Overloading Patterns

Operators are overloaded only on types with natural mathematical or comparison semantics:

```csharp
// DateTime — subtraction produces TimeSpan
public static TimeSpan operator -(DateTime d1, DateTime d2);
public static DateTime operator +(DateTime d, TimeSpan t);

// Decimal — full arithmetic operators
public static decimal operator +(decimal d1, decimal d2);
public static decimal operator -(decimal d1, decimal d2);
public static bool operator ==(decimal d1, decimal d2);
public static bool operator !=(decimal d1, decimal d2);
```

**Operator conventions:**
- Operators always come in pairs (`==`/`!=`, `<`/`>`, `<=`/`>=`)
- `IEquatable<T>` is implemented alongside `==`/`!=`
- `GetHashCode()` is overridden whenever `Equals()` is
- Named method equivalents exist (`Add`, `Subtract`, `Equals`, `CompareTo`)

## IEquatable<T> Pattern

As seen in `DateTime`, `Guid`, `Int32`, etc.:

```csharp
public readonly struct Money : IEquatable<Money>
{
    public decimal Amount { get; }
    public string Currency { get; }

    public bool Equals(Money other)
        => Amount == other.Amount && Currency == other.Currency;

    public override bool Equals(object? obj)
        => obj is Money other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Amount, Currency);

    public static bool operator ==(Money left, Money right) => left.Equals(right);
    public static bool operator !=(Money left, Money right) => !left.Equals(right);
}
```

## IComparable<T> Pattern

As seen in `String`, `DateTime`, `Int32`:

```csharp
public readonly struct Version : IComparable<Version>, IEquatable<Version>
{
    public int Major { get; }
    public int Minor { get; }

    public int CompareTo(Version other)
    {
        int result = Major.CompareTo(other.Major);
        return result != 0 ? result : Minor.CompareTo(other.Minor);
    }
}
```

## ToString Pattern

Every type should override `ToString()` with a human-readable representation:

```csharp
// DateTime
public override string ToString() => "2/12/2026 2:39:17 AM";

// Guid
public override string ToString() => "d85b1407-351d-4694-9392-03acc5870eb1";

// Custom types should follow the same pattern
public override string ToString() => $"{Name} ({Count} items)";
```

## Virtual Member Patterns

Virtual members should be used deliberately, not speculatively:

```csharp
public class HttpMessageHandler
{
    // Virtual: designed as customization point
    protected internal virtual HttpResponseMessage Send(
        HttpRequestMessage request, CancellationToken cancellationToken);
}

public abstract class Stream
{
    // Abstract: MUST be implemented
    public abstract int Read(byte[] buffer, int offset, int count);

    // Virtual: CAN be overridden (has default implementation)
    public virtual void CopyTo(Stream destination, int bufferSize) { /* default */ }

    // Non-virtual: fixed behavior
    public void Dispose() { /* fixed cleanup workflow */ }
}
```

## Member Design Checklist

- [ ] Properties are cheap and idempotent; methods are used for operations
- [ ] Overloads have consistent parameter order and naming
- [ ] Constructors support simple instantiation for common cases
- [ ] Events use `EventHandler<TEventArgs>` pattern
- [ ] No public fields (properties used instead)
- [ ] Operators come in pairs with named equivalents
- [ ] `IEquatable<T>` implemented with `GetHashCode` and `==`/`!=`
- [ ] `ToString()` provides meaningful human-readable output
- [ ] Virtual members have clear extensibility purpose
