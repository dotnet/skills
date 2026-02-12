# Naming Conventions Reference

Established C# naming conventions for public API design.

## Casing

### PascalCase

Used for all public identifiers except parameters. Every word starts with an uppercase letter.

```csharp
// Types
public class StreamReader { }
public struct DateTime { }
public interface IEnumerable<T> { }
public enum ConsoleColor { }

// Members
public int Count { get; }
public void ReadLine() { }
public event EventHandler Click;
public const int MaxValue = int.MaxValue;
```

### camelCase

Used for parameters and local variables (also for private fields with `_` prefix).

```csharp
public void CopyTo(Stream destination, int bufferSize) { }
private int _count;
private static TimeSpan s_defaultTimeout;
```

## Acronym Casing

Established convention: two-letter acronyms stay uppercase, three+ letters use PascalCase.

| Acronym | Example | Pattern |
|---------|--------|---------|
| IO | `System.IO` | Two letters → uppercase |
| UI | `UIElement` | Two letters → uppercase |
| DB | `DbConnection` | Two letters → uppercase (note: newer APIs use `Db`) |
| Html | `HtmlWriter` | Three letters → PascalCase |
| Xml | `XmlReader` | Three letters → PascalCase |
| Json | `JsonSerializer` | Four letters → PascalCase |
| Url | `UrlEncoder` | Three letters → PascalCase |

## Type Name Patterns

### Classes and Structs — Nouns

Use noun or noun phrase names:
- `FileStream`, `StringBuilder`, `HttpClient`, `MemoryCache`
- `DateTime`, `TimeSpan`, `Guid`, `Color`

### Interfaces — `I` Prefix + Adjective/Noun

Established convention:
- `IDisposable`, `IComparable`, `IFormattable` (adjectives)
- `IEnumerable<T>`, `ICollection<T>`, `IList<T>` (nouns)
- `IServiceProvider`, `ICustomFormatter` (noun phrases)

### Type Parameters — `T` Prefix

```csharp
// Single type param: just T
public class List<T> { }
public interface IComparer<T> { }

// Multiple or constrained: T + descriptive name
public class Dictionary<TKey, TValue> { }
public interface ISessionChannel<TSession> where TSession : ISession { }
```

### Suffixes

| When type... | Suffix | Examples |
|-------------|--------|-------------|
| Derives from `Exception` | `Exception` | `ArgumentNullException`, `IOException` |
| Derives from `Attribute` | `Attribute` | `ObsoleteAttribute`, `SerializableAttribute` |
| Derives from `EventArgs` | `EventArgs` | `CancelEventArgs`, `PropertyChangedEventArgs` |
| Represents a collection | `Collection` | `ObservableCollection<T>`, `KeyedCollection<K,T>` |
| Represents a dictionary | `Dictionary` | `ConcurrentDictionary<K,V>`, `SortedDictionary<K,V>` |

## Method Names — Verbs

Methods use verbs or verb phrases:

```csharp
// System.IO.Stream
public abstract int Read(byte[] buffer, int offset, int count);
public abstract void Write(byte[] buffer, int offset, int count);
public virtual void CopyTo(Stream destination);
public virtual void Close();

// System.String
public int CompareTo(string value);
public bool Contains(string value);
public string Replace(string oldValue, string newValue);
public string[] Split(char separator);
```

Async methods add `Async` suffix:
```csharp
public Task<int> ReadAsync(byte[] buffer, int offset, int count);
public Task WriteAsync(byte[] buffer, int offset, int count);
```

## Property Names — Nouns/Adjectives

```csharp
// Nouns
public int Count { get; }
public int Length { get; }
public string Name { get; set; }
public Stream BaseStream { get; }

// Boolean with Is/Can/Has
public bool IsReadOnly { get; }
public bool CanRead { get; }
public bool CanSeek { get; }
public bool HasValue { get; }
public bool IsCompleted { get; }
```

## Event Names — Verb Tense

Use present participle (gerund) for events that fire before/during, and past tense for events that fire after:

| Pre-event | Post-event |
|-----------|-----------|
| `Closing` | `Closed` |
| `Validating` | `Validated` |
| `PropertyChanging` | `PropertyChanged` |
| `CollectionChanging` | N/A (some types omit pre-event) |

## Enum Names

Non-flag enums use singular nouns:
```csharp
public enum ConsoleColor { Black, Blue, Green, ... }
public enum DayOfWeek { Sunday, Monday, ... }
public enum FileMode { Create, Open, Append, ... }
```

Flag enums use plural nouns and `[Flags]`:
```csharp
[Flags]
public enum FileAttributes { ReadOnly = 1, Hidden = 2, System = 4, ... }

[Flags]
public enum BindingFlags { Default = 0, Instance = 4, Static = 8, Public = 16, ... }
```

## Namespace Patterns

Established convention: `<Company>.<Technology>[.<Feature>]`

```
System.Collections.Generic
System.IO.Compression
System.Net.Http
System.Text.Json
Microsoft.Extensions.Logging
Microsoft.Extensions.DependencyInjection
```

## What to Avoid

These patterns should never appear in public APIs:

- Hungarian notation (`strName`, `iCount`, `bEnabled`)
- Underscores in public names (`Get_Value`, `Max_Count`)
- Abbreviations (`Btn`, `Msg`, `Mgr` — except universally known ones like `IO`)
- Names differing only by case
- Language-specific type names in methods (`GetInt` vs `GetInt32`)
