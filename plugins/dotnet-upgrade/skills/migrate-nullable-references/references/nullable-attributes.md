# Nullable Attributes Reference

When a simple `?` annotation cannot express the null contract, use attributes from `System.Diagnostics.CodeAnalysis`:

| Attribute | Use case |
|-----------|----------|
| `[NotNullWhen(true/false)]` | `TryGet` or `IsNullOrEmpty` patterns — the argument is not null when the method returns the specified bool. For `Try` methods with a **non-generic** out parameter, declare the parameter nullable and use `[NotNullWhen(true)] out MyType? result` — it is `null` on failure and non-null on success. Also add to `Equals(object? obj)` overrides to indicate the argument is non-null when returning `true` |
| `[MaybeNullWhen(true/false)]` | For `Try` methods with a **generic** out parameter, keep the parameter non-nullable and use `[MaybeNullWhen(false)] out T result` — the value may be `default` (null for reference types) on failure. Using `[NotNullWhen]` with `T?` here would change value-type signatures to `Nullable<T>` |
| `[NotNull]` | A nullable parameter is guaranteed non-null when the method returns (e.g., a `ThrowIfNull` helper) |
| `[MaybeNull]` | A non-nullable generic return might be `default` (null). Rare in practice — prefer `T?` when possible. Reserve for cases like `AsyncLocal<T>.Value` where `T?` is wrong because setting to null is invalid when `T` is non-nullable |
| `[AllowNull]` | A non-nullable property setter accepts null (e.g., falls back to a default value) |
| `[DisallowNull]` | A nullable property should never be explicitly set to null |
| `[MemberNotNull(nameof(...))]` | A helper method guarantees that specific members are non-null after it returns. When initializing multiple fields, prefer multiple `[MemberNotNull("field1")]` `[MemberNotNull("field2")]` attributes over one `[MemberNotNull("field1", "field2")]` — the `params` overload is not CLS-compliant |
| `[NotNullIfNotNull("paramName")]` | The return is non-null if the named parameter is non-null |
| `[DoesNotReturn]` | The method always throws — code after the call is unreachable |

Add `using System.Diagnostics.CodeAnalysis;` where needed.

> **Caution:** The compiler does not warn when nullable attributes are misapplied — for example, `[DisallowNull]` on an already non-nullable parameter or `[MaybeNull]` on a by-value input parameter (not `ref`/`out`) are silently ignored. Verify each attribute is placed where it has an effect.
