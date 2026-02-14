# Expected output WITH skill

This represents the type of review Claude gives with the `reviewing-dotnet-api-design` skill loaded. The review follows the 9-step structured workflow with convention-specific catches that require the reference files.

## Characteristics of a with-skill review

- **Loads the skill** — invokes `reviewing-dotnet-api-design` skill
- **Caller-first** — writes calling code that reveals `MessageEnvelope` is awkward as a struct (value-copy semantics on a large, mutable type)
- **Surface classification** — identifies as "New library API" for NuGet distribution
- **Severity grouping** — Critical → Warning → Suggestion with specific criteria
- **Catches struct triple violation** — mutability + >16 bytes + reference-type field, with size calculation
- **Catches `[Flags]` non-power-of-two** — `ExactlyOnce = 3` silently aliases the combination; flags values must be powers of two
- **Catches parameter name inconsistency** — `topicName`/`callback` vs `topic`/`handler` across methods
- **Catches async validation timing** — argument validation must happen synchronously before first `await`
- **Catches operator pair** — `==` without `!=`, `Equals`, `GetHashCode` violates operator convention
- **Catches missing `TryParse`** — `Parse` method that throws should have a `TryParse` companion
- **Notes strengths** — correct `CancellationToken` placement, `ReadOnlyMemory<byte>` for payload

## Example with-skill output

### Surface Classification

**Type:** New library API (NuGet package)
**Namespace:** `Contoso.Messaging` — pub/sub message broker

### Scenario Code

```csharp
// Scenario 1: Publish a message
var envelope = new MessageEnvelope
{
    Topic = "orders.created",
    Payload = payloadBytes,
    Headers = new Dictionary<string, string> { ["content-type"] = "application/json" },
    SentAt = DateTimeOffset.UtcNow
};
var broker = new MessageBroker();
broker.Publish(envelope);

// Scenario 2: Subscribe to a topic
broker.Subscribe("orders.created", msg => Console.WriteLine(msg.Topic));

// Scenario 3: Parse a result
var result = MessageResult.Parse("OK|");
```

**Assessment:** Scenario 1 is awkward — `MessageEnvelope` is a struct but requires initializing 5 properties including a `Dictionary`. Assigning this to another variable copies all fields, but the `Dictionary` is shared by reference. This is a class, not a struct. Scenario 2 is clean. Scenario 3 has no safe alternative if the format is wrong.

### 🔴 Critical

#### C1. `MessageEnvelope` — Mutable struct with reference-type field, exceeds 16-byte guideline

**Convention:** Structs should be ≤16 bytes, immutable (`readonly struct`), with no reference-type fields. `MessageEnvelope` has 5 properties including `Dictionary<string, string>` and `string` fields — far exceeding 16 bytes and creating value-copy confusion.

**Fix:** Convert to `sealed class` or `readonly record struct` (if immutable and small).

#### C2. `[Flags] DeliveryMode` — Values are not powers of two

**Convention:** `[Flags]` enum values must be powers of two (1, 2, 4, 8...) so bitwise combination works correctly. `ExactlyOnce = 3` silently equals `AtMostOnce | AtLeastOnce`, making it impossible to distinguish.

**Fix:**
```csharp
[Flags]
public enum DeliveryMode
{
    AtMostOnce = 1,
    AtLeastOnce = 2,
    ExactlyOnce = 4
}
```

Or remove `[Flags]` if these are mutually exclusive modes (use singular `DeliveryMode` without `[Flags]`).

#### C3. `ActiveTopics` returns `List<string>` in public API

**Convention:** Public APIs return `ReadOnlyCollection<T>` or `IReadOnlyList<T>`, not `List<T>`.

**Fix:** Return `ReadOnlyCollection<string>` or `IReadOnlyList<string>`.

### 🟡 Warning

#### W1. Parameter names inconsistent across related methods

**Convention:** Parameters representing the same concept must use identical names across all methods.

| Method | Topic param | Handler param |
|--------|------------|---------------|
| `Publish` | `envelope` (contains `.Topic`) | — |
| `Subscribe` | `topicName` ← inconsistent | `callback` ← inconsistent |
| `Unsubscribe` | `topic` | — |

**Fix:** Standardize to `topic` and `handler` across all methods.

#### W2. `PublishAsync` validates arguments inside `Task.Run`

**Convention:** Async methods must validate arguments synchronously before the first `await`. Deferring validation into the task means the caller gets a faulted `Task` instead of an immediate exception at the call site.

**Fix:**
```csharp
public Task PublishAsync(MessageEnvelope envelope, CancellationToken token)
{
    ArgumentNullException.ThrowIfNull(envelope.Topic);
    return PublishAsyncCore(envelope, token);
}

private async Task PublishAsyncCore(MessageEnvelope envelope, CancellationToken token)
{
    await Task.Run(() => Publish(envelope), token);
}
```

#### W3. `operator ==` without `!=`, `Equals`, or `GetHashCode`

**Convention:** Operators must be implemented in pairs. `==` requires `!=`, and both require consistent `Equals(object)` and `GetHashCode()` overrides.

**Fix:** Implement `!=`, override `Equals(object)`, override `GetHashCode()`, and implement `IEquatable<MessageBroker>`.

#### W4. `ArgumentNullException` without `paramName` in `Subscribe()`

**Convention:** Always pass `nameof(parameter)`. Use `ArgumentNullException.ThrowIfNull()` (.NET 6+).

#### W5. `ArgumentException` for null topic in `Publish()` — wrong exception type

**Convention:** Null arguments get `ArgumentNullException`, not `ArgumentException`.

#### W6. `BrokerException(string msg)` — abbreviated parameter name

**Convention:** No abbreviations in public APIs. Use `message` not `msg`.

#### W7. `MessageResult.Parse` throws bare `Exception`; no `TryParse` variant

**Convention:** Use `FormatException` for parse failures. Provide a `TryParse(string, out MessageResult)` companion for callers who expect failures.

#### W8. `Retrieval()` — noun-named method

**Convention:** Methods use verbs. Rename to `Retrieve()` or `GetNextMessage()`.

### 💡 Suggestion

#### S1. `MessageBroker` unsealed with no virtual members

**Convention:** Seal classes not designed for inheritance. No virtual members = not designed for extension.

#### S2. `IMessageBroker` has no async methods

For an I/O-bound broker, consider adding `PublishAsync`/`SubscribeAsync` to the interface.

#### S3. `ContainsKey` + indexer double-lookup in `Subscribe()`

**Convention:** Use `TryGetValue` for single-lookup pattern.

### ✅ Strengths

- **`CancellationToken` last** — `PublishAsync(envelope, token)` follows correct parameter ordering
- **`ReadOnlyMemory<byte>` for Payload** — Avoids mutable `byte[]` in the public surface
- **`BrokerException`** — Correct `Exception` suffix naming

### Summary

| Severity | Count | Key Issues |
|----------|-------|------------|
| 🔴 Critical | 3 | Mutable oversized struct, `[Flags]` non-power-of-two, `List<T>` return |
| 🟡 Warning | 8 | Param name inconsistency, async validation timing, unpaired operator, missing TryParse, wrong exception types, abbreviation |
| 💡 Suggestion | 3 | Unsealed class, no async interface, double-lookup |
