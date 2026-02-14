---
name: reviewing-dotnet-api-design
description: Reviews .NET API designs for consistency with established C# conventions covering naming, type design, member design, error handling, collections, extensibility, and breaking changes. Use when reviewing public API surfaces, designing new library APIs, preparing API proposals, or assessing breaking change risk.
---

# .NET API Design Review

Review .NET API surfaces against established C# conventions. Covers naming, type design, member design, error handling, extensibility, collection usage, and resource management.

You do NOT cite or reference the Pearson-licensed "Framework Design Guidelines" book or the learn.microsoft.com/en-us/dotnet/standard/design-guidelines/ pages.

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Code under review | Yes | The API surface to analyze (types, members, signatures) |
| Context | Recommended | Library, framework, or application? NuGet package or internal? |
| .NET version | Recommended | Target framework version |
| Scope | Optional | Full review or focused area (naming, types, errors, etc.) |

## Workflow

### Step 1: Classify and Write Caller Code

Classify the API surface:

| Surface type | Focus | Key risk |
|-------------|-------|----------|
| New library API | Full review | Getting the shape wrong before anyone depends on it |
| Extension to existing API | Consistency with existing surface | Inconsistency with established conventions |
| Modification to existing API | Breaking change assessment first | Breaking existing consumers |
| API proposal | Scenario code, then full review | Approving a surface that is hard to use |

Write sample calling code for the top 2-3 scenarios. If calling code is awkward in 3-5 lines, the API needs work.

### Step 2: Review Naming

Load [references/naming-conventions.md](references/naming-conventions.md) and check all public names against C# naming conventions.

### Step 3: Review Type Design

Load [references/type-design-patterns.md](references/type-design-patterns.md) and verify type choices (class vs struct vs interface vs enum).

### Step 4: Review Member Design

Load [references/member-design-patterns.md](references/member-design-patterns.md) and check properties, methods, events, constructors, and operator patterns.

### Step 5: Review Error Handling

Load [references/error-handling-patterns.md](references/error-handling-patterns.md) and check exception types, argument validation, and Try-Parse patterns.

### Step 6: Review Collections

Check collection conventions in public API surfaces:
- Return `Collection<T>` / `ReadOnlyCollection<T>`, not `List<T>`
- Accept `IEnumerable<T>` as parameters
- Return empty collections, never null

### Step 7: Review Resource Management

If the type manages resources, check for proper `IDisposable` implementation with the standard dispose pattern.

### Step 8: Review Extensibility

Check extensibility mechanisms: abstract vs virtual methods, events, delegates, sealed types. Flag unsealed types with no virtual members.

### Step 9: Assess Breaking Changes

If modifying existing APIs, load [references/api-review-checklist.md](references/api-review-checklist.md) for the breaking change assessment table.

## Output Format

1. **Surface classification**: New / Extension / Modification, library vs application
2. **Scenario code**: Calling code for top 2-3 scenarios
3. **Issues found** (by severity):
   - **Critical**: Contradicts established conventions (mutable struct, `List<T>` in public API, bare `Exception`, missing `IDisposable`)
   - **Warning**: Deviates from common C# patterns (naming mismatch, inconsistent overloads)
   - **Suggestion**: Polish improvements (more descriptive parameter names)
4. **For each issue**: Convention → what code does → recommended fix with before/after code
5. **Strengths**: What the API does well
6. **Breaking change assessment**: If modifying existing APIs

## Validation

- [ ] All public names follow C# naming conventions
- [ ] Type choices are appropriate (struct vs class vs interface)
- [ ] No `List<T>` or mutable arrays in public API surface
- [ ] Standard exception types used with `paramName`
- [ ] `IDisposable` correctly implemented where needed
- [ ] No breaking changes to existing consumers
- [ ] Calling code for top scenarios is clean and intuitive

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| `List<T>` in public API | Use `Collection<T>` or `ReadOnlyCollection<T>` |
| Mutable structs | Make value types `readonly struct` |
| Throwing bare `Exception` | Use specific types (`ArgumentNullException`, `InvalidOperationException`) |
| Missing Try-Parse variant | Provide `TryParse` alongside `Parse` for commonly-failing operations |
| Inconsistent overload parameter order | Simplest overload delegates to most complete |

## References

- [Naming Conventions](references/naming-conventions.md) — C# naming patterns and coding style
- [Type Design Patterns](references/type-design-patterns.md) — Class, struct, interface, and enum patterns
- [Member Design Patterns](references/member-design-patterns.md) — Properties, methods, events, constructors, operators
- [Error Handling Patterns](references/error-handling-patterns.md) — Exception types, Try-Parse, argument validation
- [API Review Checklist](references/api-review-checklist.md) — Checklist for API reviews and breaking change assessment
