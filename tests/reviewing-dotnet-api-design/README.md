# README

This file contains a prompt, a bad output (without skill), and a good output (with skill).

The skill is considered successful if the output looks like the bad output without the skill, and like the good output with the skill. If the output looks like the good output without the skill, the skill is considered ineffective. If the output looks like the bad output with the skill and not like the good output with the skill, the skill is considered incorrect.

## Input prompt

Review this API for consistency with .NET conventions. This is a NuGet library targeting .NET 8: `tests/reviewing-dotnet-api-design/test-api.cs`

## Test asset

`test-api.cs` — A C# file containing a `Contoso.Messaging` namespace implementing a message broker library. The code compiles and works correctly, but contains subtle API design convention violations that require .NET-specific knowledge to catch.

### Embedded violations (hard — most require convention knowledge)

| # | Violation | Severity | Category | Claude catches without skill? |
|---|-----------|----------|----------|-------------------------------|
| 1 | `MessageEnvelope` is a mutable struct >16 bytes with `Dictionary<>` field | Critical | Type design | Unlikely — compiles fine, struct size rule is .NET-specific |
| 2 | `[Flags] DeliveryMode` values aren't powers of two — `ExactlyOnce = 3` aliases `AtMostOnce | AtLeastOnce` silently | Critical | Type design | Unlikely — compiles fine, requires knowing flags must be powers of two |
| 3 | `ActiveTopics` returns `List<string>` in public API | Critical | Collections | Sometimes — code works, convention is `ReadOnlyCollection<T>` |
| 4 | `Subscribe()` uses different parameter names (`topicName`/`callback`) than `Publish()`/`Unsubscribe()` (`topic`/`handler`) — inconsistent across overloads of the same concept | Warning | Member design | Unlikely — each method compiles independently, requires cross-method convention check |
| 5 | `PublishAsync` validates arguments inside `Task.Run` instead of synchronously before first await | Warning | Error handling | Unlikely — both paths throw, requires knowing .NET async validation convention |
| 6 | `ArgumentNullException()` thrown without `paramName` in `Subscribe()` — two instances | Warning | Error handling | Sometimes — compiles fine |
| 7 | `ArgumentException("Topic is required")` instead of `ArgumentNullException(nameof(envelope.Topic))` for null topic | Warning | Error handling | Unlikely — both throw, requires knowing exception type hierarchy convention |
| 8 | `Retrieval()` method named with noun instead of verb | Warning | Naming | Sometimes — requires naming convention knowledge |
| 9 | `operator ==` defined without matching `operator !=`, `Equals`, or `GetHashCode` | Warning | Member design | Unlikely — compiles with warning only, requires knowing operator pair convention |
| 10 | `BrokerException` constructor uses `msg` parameter instead of `message` — abbreviation in public API | Warning | Naming | Unlikely — compiles fine, requires knowing abbreviation convention |
| 11 | `MessageResult.Parse()` throws bare `Exception` instead of `FormatException`; no `TryParse` variant | Warning | Error handling | Sometimes catches `Exception`; unlikely to flag missing `TryParse` |
| 12 | `IMessageBroker` interface has no async methods despite I/O-bound operations | Suggestion | Member design | Sometimes — requires domain judgment |
| 13 | `MessageBroker` unsealed with no virtual members | Suggestion | Extensibility | Sometimes |
| 14 | `ContainsKey` + indexer double-lookup in `Subscribe()` | Suggestion | Member design | Sometimes — functional but inefficient |

### Things done well (strengths)

| # | Good practice | Category |
|---|---------------|----------|
| 1 | `CancellationToken` is last parameter in `PublishAsync` | Member design |
| 2 | `BrokerException` derives from `Exception` with proper suffix | Naming |
| 3 | `ReadOnlyMemory<byte>` used for `Payload` (not `byte[]`) | Type design |

## Evaluation criteria

The skill demonstrates unique value if the review:

1. Writes sample calling code BEFORE reviewing (caller-first — the calling code should reveal that `MessageEnvelope` is awkward as a struct)
2. Classifies the API surface type (new library API)
3. Groups findings by severity (Critical → Warning → Suggestion)
4. Catches the struct size / mutability / reference-type field triple violation on `MessageEnvelope`
5. Catches the `[Flags]` enum with non-power-of-two values (`ExactlyOnce = 3`)
6. Catches the parameter name inconsistency across `Subscribe` vs `Publish`/`Unsubscribe`
7. Catches the async argument validation timing issue in `PublishAsync`
8. Catches the unpaired `operator ==` (missing `!=`, `Equals`, `GetHashCode`)
9. Flags the missing `TryParse` variant on `MessageResult.Parse`
10. Notes strengths — `CancellationToken` placement, `ReadOnlyMemory<byte>` usage
