# README

This file contains a prompt, a bad output (without skill), and a good output (with skill).

The skill is considered successful if the output looks like the bad output without the skill, and like the good output with the skill. If the output looks like the good output without the skill, the skill is considered ineffective. If the output looks like the bad output with the skill and not like the good output with the skill, the skill is considered incorrect.

## Input prompt

Review this API for consistency with .NET conventions: `tests/reviewing-dotnet-api-design/test-api.cs`

## Test asset

`test-api.cs` — A C# file containing a `Contoso.Networking` namespace with deliberate API design violations and some correctly implemented patterns.

### Embedded violations

| # | Violation | Severity | Category |
|---|-----------|----------|----------|
| 1 | `ConnectionInfo` is a mutable struct with reference-type field and side-effecting method | Critical | Type design |
| 2 | `GetItems()` returns `List<string>` in public API | Critical | Collection convention |
| 3 | `Result()` method named with a noun instead of a verb | Warning | Naming |
| 4 | `throw new ArgumentNullException()` missing `paramName` | Warning | Error handling |
| 5 | `DataProcessor` is unsealed with no virtual members | Warning | Extensibility |
| 6 | `Checksum` property does expensive computation (should be a method) | Suggestion | Member design |

### Embedded strengths

| # | Good practice | Category |
|---|---------------|----------|
| 1 | `FileWatcher` uses `EventHandler<TEventArgs>` with `protected virtual OnChanged` | Event pattern |
| 2 | `FileWatcher` implements `IDisposable` with standard dispose pattern | Resource management |
| 3 | `FileChangedEventArgs` derives from `EventArgs` with proper suffix | Naming / type design |

## Evaluation criteria

The skill adds value if the review:

1. Loads the `reviewing-dotnet-api-design` skill
2. Writes sample calling code BEFORE reviewing (caller-first methodology)
3. Classifies the API surface (new library API / extension / modification)
4. Groups findings by severity (Critical → Warning → Suggestion)
5. Catches the mutable struct as Critical (not just a warning)
6. Catches the `List<T>` return type as a convention violation (not just valid code)
7. Identifies the noun-named method with reference to naming conventions
8. Flags the missing `paramName` on `ArgumentNullException`
9. Notes strengths — correct event pattern and IDisposable implementation
10. Provides concrete before/after code fixes for each issue
