# Expected output WITHOUT skill

This represents the type of review Claude gives without the `reviewing-dotnet-api-design` skill loaded. Claude catches general code quality issues but misses .NET-specific API design conventions.

## Characteristics of a without-skill review

- **No caller-first methodology** — reviews code top-to-bottom, never writes calling code to test usability
- **No API surface classification** — doesn't identify this as a library API with different conventions than application code
- **No severity grouping** — flat list of observations
- **Misses struct size convention** — `MessageEnvelope` compiles fine; Claude won't know the ≤16 byte soft limit
- **Misses `[Flags]` power-of-two rule** — `ExactlyOnce = 3` compiles and works; requires knowing bitwise combination semantics
- **Misses parameter name consistency** — `topicName` vs `topic` across methods is invisible without cross-method convention checking
- **Misses async validation timing** — both paths throw the same exception; requires knowing the .NET convention for immediate vs deferred validation
- **Misses operator pair convention** — `operator ==` without `!=`/`Equals`/`GetHashCode` generates a warning but Claude likely won't explain the convention depth
- **Misses `TryParse` convention** — `Parse` that throws is valid; knowing to also provide `TryParse` is .NET-specific
- **Catches some obvious issues** — bare `Exception`, `List<T>` (maybe), noun-named method (maybe)

## Example without-skill output

Here are some observations about the `Contoso.Messaging` API:

1. **`MessageEnvelope` struct** — This struct has mutable properties and a `Dictionary<string, string>` field. Consider whether a class might be more appropriate since it contains reference types.

2. **`DeliveryMode` enum** — Has `[Flags]` attribute. Note that `ExactlyOnce = 3` which is the combination of the other two values — this might be intentional for bitwise combination.

3. **`Subscribe` method** — The null checks throw `ArgumentNullException` without parameter names. Consider adding `nameof()`.

4. **`Publish` method** — Throws `ArgumentException` for a null topic. The error message is okay but you might want to be more specific.

5. **`PublishAsync`** — Uses `Task.Run` which isn't ideal for a library. Consider using truly async I/O instead.

6. **`Retrieval` method** — The name is a bit unusual. Consider `Retrieve` or `GetMessage` instead.

7. **`ActiveTopics`** — Returns a `List<string>`. Consider whether an `IReadOnlyList<string>` would be more appropriate.

8. **`operator ==`** — You've defined `==` but the compiler will warn about missing `!=` and `GetHashCode`. Consider implementing those too.

9. **`MessageResult.Parse`** — Throws generic `Exception`. Use a more specific exception type.

10. **`BrokerException`** — Good use of a custom exception type.

Overall the code structure is reasonable. The main areas to improve are exception handling specificity and the struct vs class decision for `MessageEnvelope`.
