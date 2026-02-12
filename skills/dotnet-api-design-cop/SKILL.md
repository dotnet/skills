---
name: dotnet-api-design-cop
description: Reviews .NET API designs for consistency with established C# conventions. Use when reviewing public API surfaces, designing new library APIs, or checking naming, type choices, member design, error handling, and extensibility patterns against established C# conventions.
---

# .NET API Design Review

This skill provides actionable guidance for reviewing .NET API designs against established C# conventions. It covers naming conventions, type design choices, member design, error handling, extensibility, collection usage, and resource management patterns.

## When to Use

- Reviewing a public API surface for consistency with C# conventions
- Designing new types, methods, properties, or events for a .NET library
- Preparing an API proposal for review
- Checking naming consistency across a namespace or assembly
- Evaluating type design choices (class vs struct vs interface)
- Reviewing error handling and exception usage
- Assessing extensibility mechanisms
- Validating collection usage in public APIs
- Checking IDisposable implementation patterns
- Evaluating breaking change risk in API modifications

## When Not to Use

- Performance optimization (use `dotnet-jit-optimization` skill)
- Async/await pattern correctness (use `dotnet-async-patterns` skill)
- Synchronization primitive selection (use `dotnet-sync-primitives` skill)
- Internal/private code review (these conventions target public API surfaces)
- REST/HTTP API endpoint design (different domain)

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Code under review | Yes | The API surface to analyze (types, members, signatures) |
| Context | Recommended | Library, framework, or application? NuGet package or internal? |
| .NET version | Recommended | Target framework version |
| Scope | Recommended | Full review or focused area (naming, types, errors, etc.) |

## Workflow

### Step 1: Classify the API surface and write caller code

First, classify what you are reviewing:

| Surface type | Focus areas | Key risk |
|-------------|-------------|----------|
| New library API | Full review — naming, types, members, errors | Getting the shape wrong before anyone depends on it |
| Extension to existing API | Consistency with existing surface, overload patterns | Inconsistency with the established conventions of the library |
| Modification to existing API | Breaking change assessment first, then design | Breaking existing consumers |
| API proposal | Scenario code, then full review | Approving a surface that is hard to use |

Then write sample code that uses the API for its top 2-3 scenarios.

Ask:
1. Can a developer accomplish the main task in a few lines?
2. Is there one obvious type where usage starts?
3. Can users create an instance, set properties, and call methods without complex initialization?
4. Would IntelliSense lead developers to the right type?

If the calling code is awkward, the API design needs work — regardless of how clean the implementation is.

### Step 2: Review naming conventions

These are established C# naming conventions.

**Casing rules:**

| Element | Convention | Examples |
|---------|-----------|-------------|
| Types | PascalCase | `StreamReader`, `StringBuilder`, `HttpClient` |
| Methods | PascalCase | `ReadLine()`, `GetHashCode()`, `ToString()` |
| Properties | PascalCase | `Length`, `Count`, `IsReadOnly` |
| Events | PascalCase | `Click`, `PropertyChanged`, `Closed` |
| Parameters | camelCase | `buffer`, `index`, `cancellationToken` |
| Constants | PascalCase | `MaxValue`, `Empty` |
| Interfaces | `I` + PascalCase | `IDisposable`, `IEnumerable<T>`, `IComparable<T>` |
| Type parameters | `T` + PascalCase | `TKey`, `TValue`, `TResult` (or just `T` for single param) |

**Naming patterns:**

| Pattern | Convention | Examples |
|---------|-----------|-------------|
| Methods | Verb or verb phrase | `Read`, `Write`, `Parse`, `CompareTo` |
| Properties | Noun, noun phrase, or adjective | `Name`, `Count`, `IsReadOnly`, `HasValue` |
| Boolean properties | Affirmative phrasing, often `Is`/`Can`/`Has` prefix | `IsEnabled`, `CanRead`, `HasValue` |
| Events | Verb tense (gerund for pre, past for post) | `Closing`/`Closed`, `Validating`/`Validated` |
| Exception types | End with `Exception` | `ArgumentNullException`, `IOException` |
| Attribute types | End with `Attribute` | `SerializableAttribute`, `ObsoleteAttribute` |
| EventArgs types | End with `EventArgs` | `CancelEventArgs`, `PropertyChangedEventArgs` |
| Enum (non-flag) | Singular noun | `ConsoleColor`, `DayOfWeek`, `FileMode` |
| Enum (flag) | Plural noun with `[Flags]` | `FileAttributes`, `BindingFlags` |

**Common violations to flag:**
- Abbreviations or contractions in public names
- Hungarian notation (`strName`, `iCount`)
- Underscores in public member names
- Language-specific type names in method names (`GetInt` instead of `GetInt32`)
- Method names using nouns instead of verbs
- Property names using verbs instead of nouns

**Before/after example — naming review correction:**
```csharp
// BEFORE: Multiple naming violations
public class data_processor               // underscore, vague name
{
    public int GetInt(string s) { ... }   // language-specific type name, cryptic parameter
    public bool process() { ... }         // lowercase, but also: does this return success?
    public string strName { get; set; }   // Hungarian notation
}

// AFTER: Consistent naming
public class DataParser                   // PascalCase, specific noun
{
    public int GetInt32(string text) { ... }  // framework type name, descriptive parameter
    public DataResult Parse() { ... }         // PascalCase verb, clear return type
    public string Name { get; set; }          // no prefix, noun
}
```

### Step 3: Review type design choices

**When to use structs:**
`DateTime`, `TimeSpan`, `Guid`, `Point`, `Color`, `Decimal`, `Int32` — small, immutable types representing single values.

Struct suitability checklist:
- Logically represents a single value
- Instance size ≤ 16 bytes
- Immutable (or `readonly struct`)
- No need for inheritance
- Frequently allocated (value type avoids heap allocation)

**When to use classes:**
`String`, `Stream`, `HttpClient`, `List<T>` — types with identity, complex behavior, inheritance, or large size.

**When to use interfaces:**
`IEnumerable<T>`, `IDisposable`, `IComparable<T>` — cross-hierarchy contracts that both classes and structs implement.

**Check for:**
- Mutable structs (these cause subtle bugs with value-copy semantics)
- Interfaces without any implementation in the library
- Enums missing `[Flags]` when values are combinable
- Overly sealed types (`String` is sealed, `Stream` is not — seal deliberately)

**Before/after example — type design correction:**
```csharp
// BEFORE: Mutable struct with reference semantics
public struct Connection
{
    public string Host { get; set; }     // mutable
    public int Port { get; set; }        // mutable
    public List<string> Tags { get; set; } // reference type in struct
    public void Connect() { ... }        // side-effecting method on value type
}

// AFTER: Class (has identity, side effects, reference-type fields)
public class Connection : IDisposable
{
    public Connection(string host, int port) { ... }
    public string Host { get; }
    public int Port { get; }
    public void Connect() { ... }
    public void Dispose() { ... }
}
```

### Step 4: Review member design

**Properties vs methods:**
Use properties for cheap, idempotent state access and methods for operations, conversions, or expensive work.

- `stream.Length` — property (cheap, idempotent)
- `stream.Read(buffer, offset, count)` — method (operation)
- `object.ToString()` — method (conversion)
- `list.ToArray()` — method (creates new object)

**Overload patterns (as in `StringBuilder`, `Console`, etc.):**
- Consistent parameter order across overloads
- Simplest overload delegates to the most complete one
- The most-parameter overload contains the core logic

```csharp
// Standard overload pattern
public void Write(string value) => Write(value, 0, value.Length);
public void Write(string value, int startIndex) => Write(value, startIndex, value.Length - startIndex);
public void Write(string value, int startIndex, int count) { /* core */ }
```

**Constructor patterns:**
- Default constructors enable simple instantiation
- Parameterized constructors for required initialization
- `CancellationToken` always last when present

**Event patterns:**
- Use `EventHandler<TEventArgs>`
- Raise through `protected virtual void On<EventName>(EventArgs e)`
- Custom EventArgs derive from `System.EventArgs`

### Step 5: Review error handling

**Standard error reporting patterns:**

| Situation | Convention | Example |
|-----------|------------|---------|
| Null argument | `ArgumentNullException` with `paramName` | `ArgumentNullException.ThrowIfNull(path)` |
| Out-of-range value | `ArgumentOutOfRangeException` with `paramName` | `ArgumentOutOfRangeException.ThrowIfNegative(count)` |
| Invalid state | `InvalidOperationException` | Calling `Read` on a closed stream |
| Not supported | `NotSupportedException` | Calling `Write` on a read-only stream |
| After disposal | `ObjectDisposedException` | Using a disposed `HttpClient` |
| Parse failure | Try-Parse pattern | `int.Parse` throws, `int.TryParse` returns bool |

**Check for:**
- Throwing `Exception` or `SystemException` directly
- Missing `paramName` on argument exceptions
- Vague exception messages
- No Try-Parse pattern for commonly-failing operations
- Exceptions thrown from `Equals`, `GetHashCode`, or `ToString`

### Step 6: Review collection usage

**Established conventions for collections in APIs:**

| Position | Preferred type | Avoid | Example |
|----------|---------------|-------|-------------|
| Return type (writable) | `Collection<T>` | `List<T>` | `HttpHeadersCollection` |
| Return type (read-only) | `ReadOnlyCollection<T>` | `T[]` (mutable) | `ReadOnlyCollection<string>` |
| Parameter (input) | `IEnumerable<T>` | `List<T>`, `T[]` | `AddRange(IEnumerable<T>)` |
| Property (writable) | `Collection<T>`, get-only | settable `List<T>` | `Items { get; }` |
| Empty collection | `Array.Empty<T>()` or empty instance | `null` | `Enumerable.Empty<T>()` |

For deeper collection guidance, see [references/](references/).

### Step 7: Review resource management

If the type manages resources, check for proper `IDisposable` implementation.

**Standard dispose pattern (as in `Stream`, `DbConnection`, etc.):**
```csharp
public class ResourceHolder : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing) { /* release managed resources */ }
            _disposed = true;
        }
    }
}
```

### Step 8: Review extensibility

**Established extensibility patterns:**

| Pattern | Example | Purpose | When to use |
|---------|------------|---------|-------------|
| Abstract methods | `Stream.Read`, `Stream.Write` | Forced customization points | Subclass MUST provide behavior |
| Virtual methods | `HttpMessageHandler.SendAsync` | Optional customization | Subclass MAY override default behavior |
| Events | `FileSystemWatcher.Changed` | Notification extensibility | External observers, no subclassing needed |
| Delegates/Func | `List<T>.Find(Predicate<T>)` | Caller-supplied logic | One-off customization at call site |
| Sealed types | `String`, `AesGcm` | Prevent inheritance | Type is not designed for extension |

**Check for:**
- Unsealed types with no virtual members (suggests sealing was forgotten)
- Virtual members with no base implementation and no documentation of expected behavior
- Extensibility via inheritance where events or delegates would be simpler

For deeper extensibility guidance, see [references/](references/).

### Step 9: Assess breaking change risk

If modifying existing APIs, check that no existing consumer code would break:

| Change | Breaking? | Mitigation |
|--------|-----------|------------|
| Remove/rename a public type or member | Yes | Add new member, `[Obsolete]` the old one |
| Change a method's return type | Yes | Add new method with different name |
| Add a required parameter | Yes | Add an overload instead; keep the old signature |
| Add a member to an interface | Yes (pre-DIM) | Use default interface methods (.NET 5+) or add a new interface |
| Change parameter type (e.g. `string` → `ReadOnlySpan<char>`) | Yes | Add overload, keep original |
| Add a new overload | Usually no | Can break if overload resolution becomes ambiguous |
| Add a new optional parameter | Usually no | Can break binary compat (recompile required) |
| Add a new type or member | No | Safe — additive change |
| Seal a previously unsealed class | Yes | Cannot be undone |
| Change exception type thrown | Yes (behavioral) | Document and version-gate |

**Rule of thumb**: If you are unsure whether a change is breaking, treat it as breaking.

## Output Format

Structure every review as:

1. **Surface classification**: New / Extension / Modification, library vs application context
2. **Scenario code**: Calling code for top 2-3 scenarios (written in Step 1)
3. **Issues found** (grouped by severity):
   - **Critical**: Contradicts established conventions (mutable struct, `List<T>` in public API, bare `Exception`, missing `IDisposable`)
   - **Warning**: Deviates from common C# patterns (naming mismatch, inconsistent overloads, missing Try-Parse)
   - **Suggestion**: Polish improvements (more descriptive parameter names, better IntelliSense ordering)
4. **For each issue**: What the convention is → what the code does → recommended fix with before/after code
5. **Strengths**: What the API does well (always include this)
6. **Breaking change assessment**: If modifying existing APIs

## Failure Modes and Recovery

| Situation | Recovery |
|-----------|----------|
| Insufficient context (don't know if it's a library or app) | Ask the developer — guidance differs (e.g. `Collection<T>` matters for libraries, less so for app-internal code) |
| Reviewing internal/private code | Clarify scope — these conventions target public API surfaces. Internal code has more latitude. |
| Conflicting conventions | Acknowledge the inconsistency, recommend the more recent pattern |
| Performance vs. API purity tradeoff | Defer to the `dotnet-jit-expert` agent for runtime-level optimization or the `dotnet-performance-patterns-reviewer` for API usage patterns; keep the public API clean |
| Breaking change is unavoidable | Document the break, suggest `[Obsolete]` transition period, recommend a major version bump |

## Validation

- [ ] All public type and member names use PascalCase
- [ ] All parameters use camelCase
- [ ] Interface names start with `I`
- [ ] Methods use verb names; properties use noun/adjective names
- [ ] Boolean properties use affirmative phrasing
- [ ] Events use verb tense naming (gerund/past)
- [ ] Flag enums have `[Flags]` and plural names
- [ ] No `List<T>` or `Dictionary<K,V>` in public API surface
- [ ] Collections return empty (not null) when empty
- [ ] Standard exception types used with `paramName` set
- [ ] `IDisposable` pattern correctly implemented where needed
- [ ] Overloaded methods have consistent parameter ordering
- [ ] No breaking changes to existing consumers
- [ ] Calling code for top scenarios is clean and intuitive

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| `List<T>` in public API | Use `Collection<T>` or `ReadOnlyCollection<T>` |
| Public fields | Convert to properties |
| Returning null from collection properties | Return empty collection |
| Method names using nouns | Methods are actions — use verbs |
| Throwing bare `Exception` | Use specific types (`ArgumentNullException`, `InvalidOperationException`) |
| Mutable structs | Make value types readonly/immutable |
| Missing Try-Parse variant | Provide `TryParse` alongside `Parse` for commonly-failing ops |
| No `paramName` on argument exceptions | Always set via constructor or `ThrowIfNull` |
| Inconsistent overload parameter order | Align all overloads; simplest delegates to most complete |

## References

For deeper guidance on specific topics, see:
- [Naming Conventions](references/naming-conventions.md) — C# naming patterns and coding style
- [Type Design Patterns](references/type-design-patterns.md) — Class, struct, interface, and enum patterns
- [Member Design Patterns](references/member-design-patterns.md) — Properties, methods, events, constructors, operators
- [Error Handling Patterns](references/error-handling-patterns.md) — Exception types, Try-Parse, argument validation
- [API Review Checklist](references/api-review-checklist.md) — Checklist for API reviews and proposal preparation
