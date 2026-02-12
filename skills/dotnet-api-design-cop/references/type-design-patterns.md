# Type Design Patterns Reference

Established C# type design conventions.

## When to Use Structs

Use structs for small, immutable types that represent single values.

| Struct | Size | Characteristics |
|--------|------|----------------|
| `Int32` | 4 bytes | Primitive value |
| `DateTime` | 8 bytes | Immutable, value semantics |
| `TimeSpan` | 8 bytes | Immutable, value semantics |
| `Guid` | 16 bytes | Immutable, value identity |
| `Decimal` | 16 bytes | Immutable, numeric value |
| `Point` | 8 bytes | Immutable, coordinate pair |
| `Color` | 4 bytes | Immutable, ARGB value |
| `CancellationToken` | 8 bytes | Lightweight, passed by value |
| `ReadOnlySpan<T>` | 16 bytes | ref struct, zero-allocation view |

**Consistent struct characteristics:**
- Small (≤ 16 bytes typically)
- Immutable (use `readonly struct`)
- Represent a single logical value
- Value equality semantics (`Equals`/`GetHashCode` based on content)
- No inheritance needed
- Rarely boxed in typical usage

```csharp
// Established struct pattern
public readonly struct Point : IEquatable<Point>
{
    public double X { get; }
    public double Y { get; }

    public Point(double x, double y) => (X, Y) = (x, y);

    public bool Equals(Point other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is Point other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);
}
```

## When to Use Classes

Use classes for types with identity, complex behavior, large size, or inheritance.

| Class | Why Not Struct |
|-------|---------------|
| `String` | Variable size, reference semantics, sealed |
| `Stream` | Abstract base, many subclasses, manages resources |
| `HttpClient` | Complex state, disposable, large |
| `List<T>` | Mutable, variable size, reference semantics |
| `Exception` | Inheritance hierarchy, reference identity |
| `Task<T>` | Shared state, awaited from multiple locations |

## When to Use Interfaces

Interfaces are used for cross-hierarchy contracts that multiple unrelated types implement.

```csharp
// IDisposable — implemented by classes (Stream, HttpClient) and some structs
public interface IDisposable
{
    void Dispose();
}

// IEnumerable<T> — implemented by List<T>, Array, Dictionary<K,V>, etc.
public interface IEnumerable<T> : IEnumerable
{
    IEnumerator<T> GetEnumerator();
}

// IComparable<T> — implemented by String, Int32, DateTime, etc.
public interface IComparable<T>
{
    int CompareTo(T other);
}
```

**Interface conventions:**
- Every interface should have multiple implementations
- Interfaces are consumed by other APIs (`IEnumerable<T>` consumed by LINQ)
- New interfaces are added cautiously (adding members breaks implementors)
- `I` prefix is universal and mandatory

## Abstract Class Patterns

Use abstract classes when shared implementation is needed alongside enforced customization:

```csharp
// Stream — abstract base with shared logic + abstract customization points
public abstract class Stream : IDisposable, IAsyncDisposable
{
    // Abstract: derived types MUST implement
    public abstract int Read(byte[] buffer, int offset, int count);
    public abstract void Write(byte[] buffer, int offset, int count);
    public abstract long Length { get; }

    // Virtual: shared default with optional override
    public virtual void CopyTo(Stream destination) { /* default impl */ }
    public virtual void Close() { Dispose(true); }

    // Concrete: shared behavior
    public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
}
```

## Enum Patterns

### Non-Flag Enums (Singular Noun)
```csharp
public enum ConsoleColor
{
    Black = 0,
    DarkBlue = 1,
    DarkGreen = 2,
    // ...
}

public enum FileMode
{
    CreateNew = 1,
    Create = 2,
    Open = 3,
    OpenOrCreate = 4,
    Truncate = 5,
    Append = 6,
}
```

### Flag Enums (Plural Noun + `[Flags]` + Powers of Two)
```csharp
[Flags]
public enum FileAttributes
{
    ReadOnly = 0x0001,
    Hidden = 0x0002,
    System = 0x0004,
    Directory = 0x0010,
    Archive = 0x0020,
    Normal = 0x0080,
}

// Usage: var attrs = FileAttributes.ReadOnly | FileAttributes.Hidden;
```

## Sealed vs Unsealed

Established convention: most types are unsealed. Sealing is used selectively.

| Sealed | Why |
|--------|-----|
| `String` | Immutable invariants, security |
| `AesGcm` | Cryptographic safety, prevent insecure overrides |
| `Tuple<T>` | No extensibility needed |

| Unsealed | Why |
|----------|-----|
| `Stream` | Designed for subclassing |
| `HttpMessageHandler` | Extensibility point |
| `Collection<T>` | Designed for customization |
| `Exception` | Custom exception types derive from it |

## Static Class Patterns

Use static classes as utility containers:

```csharp
public static class Math
{
    public static double Sqrt(double d) { ... }
    public static int Max(int val1, int val2) { ... }
}

public static class Console
{
    public static void WriteLine(string value) { ... }
    public static string ReadLine() { ... }
}

public static class Path
{
    public static string Combine(string path1, string path2) { ... }
    public static string GetExtension(string path) { ... }
}
```

## Type Design Checklist

- [ ] Struct types are small (≤ 16 bytes), immutable, and represent single values
- [ ] No mutable structs
- [ ] Structs implement `IEquatable<T>` with value-based equality
- [ ] Interfaces have multiple implementations and consumers
- [ ] Abstract classes provide shared implementation + customization points
- [ ] Flag enums have `[Flags]` attribute and power-of-two values
- [ ] Non-flag enums use singular names, flag enums use plural names
- [ ] Types are unsealed unless there's a specific reason to seal
- [ ] Static classes have a cohesive, single-purpose API surface
