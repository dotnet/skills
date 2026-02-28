# C# 15 Compiler Breaking Changes (.NET 11)

These breaking changes are introduced by the Roslyn compiler shipping with the .NET 11 SDK. They affect all projects targeting `net11.0` (which uses C# 15 by default). These are maintained separately from the runtime breaking changes at: https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/breaking-changes/compiler%20breaking%20changes%20-%20dotnet%2011

> **Note:** .NET 11 is in preview. Additional compiler breaking changes may be introduced in later previews.

## Source-Incompatible Changes

### Span/ReadOnlySpan collection expression safe-context changed to `declaration-block`

**Impact: Medium.** The safe-context of a collection expression of `Span<T>` or `ReadOnlySpan<T>` type is now `declaration-block`, matching the specification. Previously the compiler incorrectly used `function-member`.

This can cause new errors when assigning a span collection expression created in an inner scope to a variable in an outer scope:

```csharp
// BREAKS — new error
scoped Span<int> items1 = default;
foreach (var x in new[] { 1, 2 })
{
    Span<int> items = [x];
    if (x == 1)
        items1 = items; // error: safe-context is declaration-block
}

// FIX option 1: Use an array type
foreach (var x in new[] { 1, 2 })
{
    int[] items = [x];
    if (x == 1)
        items1 = items; // ok, using int[] conversion to Span<int>
}

// FIX option 2: Move collection expression to outer scope
Span<int> items = [0];
foreach (var x in new[] { 1, 2 })
{
    items[0] = x;
    if (x == 1)
        items1 = items; // ok
}
```

See also: https://github.com/dotnet/csharplang/issues/9750

### `ref readonly` synthesized delegates require `InAttribute`

**Impact: Low.** When the compiler synthesizes a delegate type for a `ref readonly`-returning method or lambda, it now properly emits metadata requiring `System.Runtime.InteropServices.InAttribute`.

```csharp
// May cause CS0518 if InAttribute is not available
var methodDelegate = this.MethodWithRefReadonlyReturn;
int x = 0;
var lambdaDelegate = ref readonly int () => ref x;
```

**Fix:** Add a reference to an assembly defining `System.Runtime.InteropServices.InAttribute` (typically available via the default runtime references).

### `ref readonly` local functions require `InAttribute`

**Impact: Low.** Same as above but for `ref readonly`-returning local functions.

```csharp
void Method()
{
    ref readonly int local() => ref x;  // CS0518 if InAttribute missing
}
```

**Fix:** Same as above — ensure `InAttribute` is available.

### Dynamic `&&`/`||` with interface left operand disallowed

**Impact: Low.** The compiler now reports a compile-time error when an interface type with `true`/`false` operators is used as the left operand of `&&` or `||` with a `dynamic` right operand. Previously this compiled but threw `RuntimeBinderException` at runtime.

```csharp
interface I1
{
    static bool operator true(I1 x) => false;
    static bool operator false(I1 x) => false;
}

class C1 : I1
{
    public static C1 operator &(C1 x, C1 y) => x;
    public static bool operator true(C1 x) => false;
    public static bool operator false(C1 x) => false;
}

void M()
{
    I1 x = new C1();
    dynamic y = new C1();
    _ = x && y; // error CS7083
}
```

**Fix:** Cast the left operand to a concrete type or to `dynamic`:
```csharp
_ = (C1)x && y;      // valid
_ = (dynamic)x && y;  // valid
```

See also: https://github.com/dotnet/roslyn/issues/80954

### `nameof(this.)` in attributes disallowed

**Impact: Low.** Using `this` or `base` inside `nameof` in an attribute is now properly disallowed per the language specification. This was unintentionally permitted since C# 12.

```csharp
// Before (.NET 10) — compiled but was unintentionally permitted
class C
{
    string P;
    [System.Obsolete(nameof(this.P))]
    void M() { }
}
```

```csharp
// After (.NET 11) — remove 'this.' qualifier
class C
{
    string P;
    [System.Obsolete(nameof(P))]
    void M() { }
}
```

See also: https://github.com/dotnet/roslyn/issues/82251

### `with()` as collection expression element (C# 15)

**Impact: Low.** When `LangVersion` is 15 or greater, `with(...)` as an element in a collection expression is treated as constructor/factory arguments (the new "collection expression arguments" feature), not as a call to a method named `with`.

```csharp
object x, y, z = ...;
object[] items;

items = [with(x, y), z];   // C# 14: call to with() method; C# 15: error
items = [@with(x, y), z];  // fix: escape to call method named 'with'
```

### Parsing of `with` in switch-expression-arm

**Impact: Low.** In a switch expression, `(X.Y) when` is now parsed as a constant pattern `(X.Y)` followed by a `when` clause. Previously it was parsed as a cast expression casting `when` to `(X.Y)`.

See also: https://github.com/dotnet/roslyn/issues/81837

## New Language Features (non-breaking but relevant)

### Collection expression arguments

C# 15 adds `with(...)` syntax as the first element of a collection expression, allowing constructor/factory arguments:

```csharp
List<string> names = [with(capacity: values.Count * 2), .. values];
HashSet<string> set = [with(StringComparer.OrdinalIgnoreCase), "Hello", "HELLO"];
```

This is the feature that causes the `with()` method call breaking change above.
