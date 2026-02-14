# Expected output WITHOUT skill

This represents the type of review Claude gives without the `reviewing-dotnet-api-design` skill loaded. The review is generic — it catches some issues but misses convention-specific violations, has no structured methodology, and lacks severity classification.

## Characteristics of a without-skill review

- **No caller-first methodology** — jumps straight into reviewing code, never writes sample calling code
- **No API surface classification** — doesn't identify whether this is a new API, extension, or modification
- **No severity grouping** — lists issues in arbitrary order without Critical/Warning/Suggestion classification
- **Misses `List<T>` convention** — the code compiles and works fine, so a generic review won't flag `List<T>` as a public API issue
- **May not flag the mutable struct** — `ConnectionInfo` is valid C# and works as written; flagging it requires knowing the value-type-with-reference-semantics convention
- **Misses `paramName`** — `throw new ArgumentNullException()` compiles fine; knowing to require `paramName` is convention-specific
- **No breaking change assessment** — doesn't evaluate what changes would break consumers
- **No reference to specific conventions** — generic advice like "consider making this immutable" without citing established patterns

## Example without-skill output

Here are some observations about this code:

1. **`ConnectionInfo` struct** — Consider making this immutable. Mutable structs can lead to confusing behavior with value copy semantics. The `Tags` list is a reference type which may cause unexpected sharing.

2. **`DataProcessor.Result` method** — The name `Result` is a bit unclear. Consider renaming to something more descriptive like `ProcessData` or `ComputeResult`.

3. **Null check** — You're throwing `ArgumentNullException` without specifying the parameter name. Consider using `throw new ArgumentNullException(nameof(data))`.

4. **`GetItems` return type** — Looks fine, returns a `List<string>`.

5. **`Checksum` property** — This property appears to do expensive work (thread sleep and hash computation). Properties should generally be lightweight.

6. **`FileWatcher`** — Good use of the dispose pattern and events.

Overall the code looks reasonable. Consider adding XML documentation comments and making `ConnectionInfo` a class instead of a struct if it needs mutable behavior.
