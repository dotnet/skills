# Member Design Patterns Reference

## Properties vs Methods

### Use Properties When:
- Access is cheap (field-like)
- Calling twice returns the same value
- No observable side effects

### Use Methods When:
- The operation is a conversion (`ToString()`, `ToArray()`)
- The call is expensive or has side effects
- Returns a new object or array

## Method Overloading Patterns

**Key overload rules:**
1. Parameter order is consistent across all overloads
2. Simpler overloads delegate to the most complete one
3. Parameter names are identical across overloads
4. `CancellationToken` is always the last parameter
5. `params` array overload is the most flexible variant

## Constructor Patterns

```csharp
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

```csharp
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

public event EventHandler<FileChangedEventArgs> FileChanged;

protected virtual void OnFileChanged(FileChangedEventArgs e)
{
    FileChanged?.Invoke(this, e);
}
```

**Conventions:**
- `EventHandler<TEventArgs>` is the standard delegate type
- Raising method is `On<EventName>`, `protected virtual`
- `EventArgs.Empty` used when no data is needed
- EventArgs properties are typically read-only

## Operator Overloading Patterns

Only on types with natural mathematical or comparison semantics:

```csharp
public static TimeSpan operator -(DateTime d1, DateTime d2);
public static DateTime operator +(DateTime d, TimeSpan t);

public static decimal operator +(decimal d1, decimal d2);
public static bool operator ==(decimal d1, decimal d2);
public static bool operator !=(decimal d1, decimal d2);
```

**Rules:**
- Operators always come in pairs (`==`/`!=`, `<`/`>`, `<=`/`>=`)
- `IEquatable<T>` is implemented alongside `==`/`!=`
- `GetHashCode()` is overridden whenever `Equals()` is
- Named method equivalents exist (`Add`, `Subtract`, `Equals`, `CompareTo`)

## IEquatable<T> Pattern

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

Every type should override `ToString()` with a human-readable representation.

## Virtual Member Patterns

Virtual members should be used deliberately, not speculatively:

```csharp
public abstract class Stream
{
    public abstract int Read(byte[] buffer, int offset, int count);
    public virtual void CopyTo(Stream destination, int bufferSize) { /* default */ }
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
