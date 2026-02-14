# API Review Checklist Reference

## Proposal Template

```markdown
## API Proposal: [Feature Name]

### Background and motivation

[Why is this needed? What problem does it solve?]

### API Usage

```csharp
// Show the top 2-3 scenarios as calling code
var processor = new DataProcessor();
processor.Configure("setting");
var result = await processor.ProcessAsync(data, cancellationToken);
```

### Proposed API

```csharp
namespace System.Data
{
    public class DataProcessor
    {
        public DataProcessor();
        public void Configure(string setting);
        public Task<Result> ProcessAsync(
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default);
    }
}
```

### Alternative designs

[What else was considered and why was it rejected?]

### Risks

[Breaking changes, compatibility, performance implications]
```

## Pre-Review: Scenario Validation

- [ ] Top 2-3 scenarios defined with sample calling code
- [ ] Calling code is clean and fits in a few lines
- [ ] There is one clear entry-point type for the feature
- [ ] Simple instantiation works (default constructor or minimal params)
- [ ] API would be discoverable through IntelliSense
- [ ] An unfamiliar developer could use it without reading documentation

## Naming Review

### Types
- [ ] PascalCase for all type names
- [ ] Classes/structs use nouns or noun phrases
- [ ] Interfaces start with `I` + adjective/noun
- [ ] Exception types end with `Exception`
- [ ] Attribute types end with `Attribute`
- [ ] Collection types end with `Collection` or `Dictionary`
- [ ] EventArgs types end with `EventArgs`
- [ ] Non-flag enums singular, flag enums plural with `[Flags]`

### Members
- [ ] Methods use verbs or verb phrases
- [ ] Properties use nouns, noun phrases, or adjectives
- [ ] Boolean properties use `Is`/`Can`/`Has` prefix where appropriate
- [ ] Events use gerund (pre) / past tense (post) naming
- [ ] Async methods end with `Async`
- [ ] No public fields — properties used instead

### Parameters
- [ ] camelCase for all parameters
- [ ] Consistent names across overloads and interface implementations
- [ ] `CancellationToken` is last parameter
- [ ] `paramName` set on all argument exceptions

### General
- [ ] No abbreviations or contractions
- [ ] No Hungarian notation
- [ ] No underscores in public names
- [ ] No names differing only by case
- [ ] Acronyms: 2-letter uppercase, 3+ PascalCase

## Type Design Review

- [ ] Struct choice justified (small, immutable, value semantics)
- [ ] No mutable structs
- [ ] Structs implement `IEquatable<T>` with value equality
- [ ] Interfaces have implementations and consumers
- [ ] Abstract classes have concrete implementations
- [ ] Types unsealed unless specific reason to seal
- [ ] Each type is a cohesive set of related members

## Member Design Review

- [ ] Properties are cheap and idempotent
- [ ] Methods used for operations, conversions, expensive work
- [ ] Overloads have consistent parameter order
- [ ] Simplest overload delegates to most complete
- [ ] Constructors support simple instantiation
- [ ] Events use `EventHandler<TEventArgs>` pattern
- [ ] Operators come in pairs with named equivalents
- [ ] `GetHashCode` overridden alongside `Equals`

## Error Handling Review

- [ ] Standard exception types used
- [ ] No direct `Exception` or `SystemException` throwing
- [ ] `paramName` set on argument exceptions
- [ ] Exception messages are clear and actionable
- [ ] Try-Parse pattern for commonly-failing operations
- [ ] Arguments validated synchronously in async methods
- [ ] `Equals`/`GetHashCode`/`ToString` don't throw

## Collection Review

- [ ] No `List<T>` or `Dictionary<K,V>` in public API surface
- [ ] `Collection<T>`/`ReadOnlyCollection<T>` for return types
- [ ] `IEnumerable<T>` for input parameters
- [ ] Collection properties are get-only
- [ ] Empty returned instead of null
- [ ] Collections preferred over arrays

## Resource Management Review

- [ ] `IDisposable` implemented if holding disposable/unmanaged resources
- [ ] `Dispose(bool)` pattern used correctly
- [ ] `GC.SuppressFinalize(this)` called in Dispose()
- [ ] `ObjectDisposedException` thrown from post-disposal usage
- [ ] `IAsyncDisposable` considered for async resource cleanup

## Breaking Change Assessment

**Breaking (avoid):**
- [ ] No types/members removed or renamed
- [ ] No method signatures changed
- [ ] No members added to interfaces
- [ ] No return types changed
- [ ] No exception types changed for existing conditions

**Safe:**
- Adding new types ✔️
- Adding new members to classes ✔️
- Adding new overloads ✔️
- Adding new enum values ⚠️ (can break switch statements)
- Adding optional parameters ⚠️ (can cause source-level breaks in some cases)
