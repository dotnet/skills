# Type Design Patterns Reference

## When to Use Structs

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

**Struct requirements:**
- Small (≤ 16 bytes typically)
- Immutable (use `readonly struct`)
- Represent a single logical value
- Value equality semantics
- No inheritance needed

```csharp
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

| Class | Why Not Struct |
|-------|---------------|
| `String` | Variable size, reference semantics, sealed |
| `Stream` | Abstract base, many subclasses, manages resources |
| `HttpClient` | Complex state, disposable, large |
| `List<T>` | Mutable, variable size, reference semantics |
| `Exception` | Inheritance hierarchy, reference identity |
| `Task<T>` | Shared state, awaited from multiple locations |

## When to Use Interfaces

```csharp
public interface IDisposable
{
    void Dispose();
}

public interface IEnumerable<T> : IEnumerable
{
    IEnumerator<T> GetEnumerator();
}

public interface IComparable<T>
{
    int CompareTo(T other);
}
```

**Interface conventions:**
- Every interface should have multiple implementations
- New interfaces are added cautiously (adding members breaks implementors)

## Abstract Class Patterns

```csharp
public abstract class Stream : IDisposable, IAsyncDisposable
{
    // Abstract: derived types MUST implement
    public abstract int Read(byte[] buffer, int offset, int count);
    public abstract void Write(byte[] buffer, int offset, int count);
    public abstract long Length { get; }

    // Virtual: shared default with optional override
    public virtual void CopyTo(Stream destination) { /* default impl */ }
    public virtual void Close() { Dispose(true); }

    public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
}
```

## Enum Patterns
Flag enums use plural nouns, `[Flags]` attribute, and power-of-two values:

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
```

## Sealed vs Unsealed

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

## Static Classes

Static classes serve as utility containers (e.g., `Math`, `Console`, `Path`).

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
