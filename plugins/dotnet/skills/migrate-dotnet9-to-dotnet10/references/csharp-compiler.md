# C# 14 Compiler Breaking Changes (.NET 10)

These breaking changes are introduced by the Roslyn compiler shipping with the .NET 10 SDK. They affect all projects targeting `net10.0` (which uses C# 14 by default). These are maintained separately from the runtime breaking changes at: https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/breaking-changes/compiler%20breaking%20changes%20-%20dotnet%2010

## Source-Incompatible Changes

### `field` keyword in property accessors

**Impact: High.** `field` is now a contextual keyword inside property `get`, `set`, and `init` accessors (for the semi-auto properties feature).

Two diagnostics apply:
- **CS9258** (warning, VS 17.12+): `field` binds to the synthesized backing field instead of an existing member (e.g., a class field named `field`). Use `this.field` or `@field` to refer to the member.
- **CS9272** (error, VS 17.14+): A local variable or nested-function parameter named `field` is **disallowed** inside a property accessor. Rename the variable or use `@field`.

```csharp
// BREAKS — CS9272 error
public object Property
{
    get
    {
        int field = 0;       // error: 'field' is a keyword in a property accessor
        return @field;
    }
}

// Also BREAKS — CS9272 error
public string Name
{
    get
    {
        payload.TryGetProperty("field", out var field);  // error: local named 'field'
        return field.GetString();
    }
}
```

**Fix options:**
1. Rename the variable: `var fieldValue = ...`, `var fieldElem = ...`
2. Escape the identifier: `var @field = ...` (compiles but less readable)
3. For class members named `field`, qualify with `this.field` or `@field`

### `extension` contextual keyword

**Impact: Medium.** Starting in C# 14, `extension` is a contextual keyword for extension containers. Code that uses `extension` as a type name, constructor, or return type will break.

```csharp
// BREAKS in C# 14
class extension { }                    // type cannot be named "extension"
using extension = SomeNamespace.Foo;   // alias cannot be named "extension"
class C<extension> { }                 // type parameter cannot be named "extension"
```

**Fix:** Rename the type, or escape as `@extension`.

### `Span<T>` and `ReadOnlySpan<T>` overloads applicable in more scenarios

**Impact: Medium.** C# 14 introduces new built-in span conversions and type inference rules. Different overloads may be chosen, and new ambiguity errors can arise.

Common patterns affected:
```csharp
// Ambiguity — Assert.Equal<T>(T[], T[]) vs Assert.Equal<T>(ReadOnlySpan<T>, Span<T>)
var x = new long[] { 1 };
Assert.Equal([2], x);               // ambiguous
Assert.Equal([2], x.AsSpan());      // fix

// Enumerable.Reverse now resolves to MemoryExtensions.Reverse (in-place, returns void)
int[] arr = [1, 2, 3];
var reversed = arr.Reverse();        // BREAKS: resolves to Span extension, not Enumerable
var reversed = Enumerable.Reverse(arr); // fix

// ReadOnlySpan preferred over Span — MemoryMarshal.Cast may fail
Span<ulong> y = MemoryMarshal.Cast<double, ulong>(x);          // BREAKS
Span<ulong> y = MemoryMarshal.Cast<double, ulong>(x.AsSpan()); // fix

// ArrayTypeMismatchException with covariant arrays
string[] s = ["a"];
object[] o = s;
C.R(o);                // BREAKS at runtime: Span<T> ctor throws ArrayTypeMismatchException
C.R(o.AsEnumerable()); // fix
```

**Fix options:**
- Add `.AsSpan()`, `.AsEnumerable()`, or explicit casts to disambiguate
- Call static methods explicitly (e.g., `Enumerable.Reverse(arr)`)
- API authors: use `[OverloadResolutionPriority]` attribute

> **Note:** The span overload resolution change is also listed in the runtime breaking changes (`core-libraries.md`). This entry provides the full Roslyn-side detail.

### Other low-impact source changes

- **`scoped` in lambda parameters**: Always treated as a modifier. If you have a ref struct type named `scoped`, escape as `@scoped`.
- **`partial` as return type**: Cannot use a type named `partial` as a return type. Escape as `@partial`.

## Behavioral Changes

### Enumerator state set to "after" during disposal

`MoveNext()` on a disposed enumerator now properly returns `false` without executing further user code. Previously, the state machine allowed resuming execution after disposal.

```csharp
var enumerator = GetItems().GetEnumerator();
enumerator.MoveNext();  // True, yields 1
enumerator.Dispose();
enumerator.MoveNext();  // now returns False (previously could continue)
```

### Diagnostics reported for pattern-based disposal in `foreach`

Obsolete `DisposeAsync` methods on enumerator types are now reported in `await foreach`. Previously these diagnostics were silently ignored.

### Redundant pattern warning in `or` patterns

The compiler now warns when the second pattern in a disjunctive `or` is redundant due to precedence:
```csharp
_ = o is not null or 42;     // warning: pattern "42" is redundant
_ = o is not int or string;  // warning: pattern "string" is redundant
// Likely intended: is not (null or 42) / is not (int or string)
```
