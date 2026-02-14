# Expected output WITH skill

This represents the type of review Claude gives with the `reviewing-dotnet-api-design` skill loaded. The review follows the 9-step structured workflow, uses caller-first methodology, classifies findings by severity, and references specific conventions.

## Characteristics of a with-skill review

- **Loads the skill** — invokes `reviewing-dotnet-api-design` skill before starting the review
- **Caller-first** — writes sample calling code for top scenarios before analyzing the API
- **Surface classification** — identifies this as a "New library API" requiring full review
- **Severity grouping** — Critical → Warning → Suggestion ordering
- **Catches `List<T>`** — flags as Critical because `List<T>` exposes internal implementation; convention is `Collection<T>` or `ReadOnlyCollection<T>`
- **Catches mutable struct** — flags as Critical because `ConnectionInfo` has mutable properties, reference-type fields, and side-effecting methods
- **Convention-specific naming** — identifies `Result()` as noun-named method, cites verb convention
- **Error handling conventions** — flags missing `paramName`, recommends `ArgumentNullException.ThrowIfNull`
- **Notes strengths** — praises correct event pattern and IDisposable implementation
- **Before/after fixes** — concrete code examples for each issue

## Example with-skill output

### Surface Classification

**Type:** New library API
**Context:** `Contoso.Networking` namespace — appears to be a library for network connectivity

### Scenario Code

```csharp
// Scenario 1: Create a connection
var info = new ConnectionInfo { Host = "example.com", Port = 443 };
info.Connect();

// Scenario 2: Process data
var processor = new DataProcessor();
string encoded = processor.Result(data);
List<string> items = processor.GetItems();

// Scenario 3: Watch for file changes
using var watcher = new FileWatcher();
watcher.Changed += (s, e) => Console.WriteLine(e.FilePath);
```

**Assessment:** Scenario 1 is problematic — `ConnectionInfo` is a value type, so `info.Connect()` operates on a copy in some contexts. Scenario 2 has awkward naming (`Result` is unclear) and leaks internal `List<T>`. Scenario 3 is clean.

### 🔴 Critical

#### C1. Mutable Struct with Reference Semantics — `ConnectionInfo`

**Convention:** Structs should be small, immutable, represent single values, have no reference-type fields, and have no side effects. (`DateTime`, `TimeSpan`, `Guid` are the model.)

**Issue:** `ConnectionInfo` is mutable (`set` accessors), has a `List<string>` field (reference type causes unexpected sharing on copy), and has a `Connect()` method (side effect on value type).

**Fix:**
```csharp
// ❌ Before
public struct ConnectionInfo { ... }

// ✅ After — Class with identity and side effects
public sealed class ConnectionInfo : IDisposable
{
    public ConnectionInfo(string host, int port) { ... }
    public string Host { get; }
    public int Port { get; }
    public void Connect() { ... }
    public void Dispose() { ... }
}
```

#### C2. `List<T>` Return Type in Public API — `DataProcessor.GetItems()`

**Convention:** Public APIs return `Collection<T>` or `ReadOnlyCollection<T>`, not `List<T>`. `List<T>` exposes `Add`, `Sort`, `Reverse` etc. which callers should not use to mutate the internal collection.

**Fix:**
```csharp
// ❌ Before
public List<string> GetItems() => new List<string>();

// ✅ After
public ReadOnlyCollection<string> GetItems() => new List<string>().AsReadOnly();
```

### 🟡 Warning

#### W1. Noun-Named Method — `DataProcessor.Result()`

**Convention:** Methods use verbs or verb phrases (`Read`, `Parse`, `ComputeHash`). Properties use nouns.

**Fix:** Rename to `ComputeResult(byte[] data)` or `Encode(byte[] data)`.

#### W2. Missing `paramName` on ArgumentNullException

**Convention:** Always pass `nameof(parameter)` or use `ArgumentNullException.ThrowIfNull()`.

**Fix:**
```csharp
// ❌ Before
throw new ArgumentNullException();

// ✅ After
ArgumentNullException.ThrowIfNull(data);
```

#### W3. Unsealed Leaf Class — `DataProcessor`

**Convention:** Classes with no virtual members and not designed for inheritance should be sealed.

**Fix:** Add `sealed` modifier: `public sealed class DataProcessor`

### 💡 Suggestion

#### S1. Property Does Expensive Work — `DataProcessor.Checksum`

**Convention:** Properties should be cheap and idempotent. Expensive operations should be methods (`ComputeChecksum()`).

**Fix:** Convert to method: `public byte[] ComputeChecksum()`

### ✅ Strengths

- **Correct event pattern** — `FileWatcher` uses `EventHandler<FileChangedEventArgs>` with `protected virtual OnChanged()`
- **Standard IDisposable** — `FileWatcher` implements the full dispose pattern with `Dispose(bool)` and `GC.SuppressFinalize`
- **Proper EventArgs** — `FileChangedEventArgs` derives from `EventArgs` with correct suffix naming

### Summary

| Severity | Count | Issues |
|----------|-------|--------|
| 🔴 Critical | 2 | Mutable struct, `List<T>` in public API |
| 🟡 Warning | 3 | Noun method name, missing paramName, unsealed class |
| 💡 Suggestion | 1 | Expensive property |
