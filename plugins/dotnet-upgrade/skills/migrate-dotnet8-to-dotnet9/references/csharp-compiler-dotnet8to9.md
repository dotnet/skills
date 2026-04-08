# C# 13 Compiler Breaking Changes (.NET 9)

These breaking changes are introduced by the Roslyn compiler shipping with the .NET 9 SDK. They affect all projects targeting `net9.0` (which uses C# 13 by default). These are maintained separately from the runtime breaking changes at: https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/breaking-changes/compiler%20breaking%20changes%20-%20dotnet%209

## Source-Incompatible Changes

### InlineArray attribute on record structs is disallowed

**Impact: Medium.** You can no longer apply the `[InlineArray]` attribute to `record struct` types. This produces error CS9259.

```csharp
// BREAKS — CS9259 error
[System.Runtime.CompilerServices.InlineArray(10)]
record struct Buffer()
{
    private int _element0;
}
```

**Fix:** Change `record struct` to a plain `struct`:
```csharp
[System.Runtime.CompilerServices.InlineArray(10)]
struct Buffer
{
    private int _element0;
}
```

### Iterators introduce safe context in C# 13

**Impact: Low–Medium.** Although the language spec states that iterators introduce a safe context, Roslyn did not enforce this in C# 12 and lower. In C# 13, iterators always introduce a safe context, which can break scenarios where unsafe context was inherited by nested local functions.

```csharp
// BREAKS in C# 13
unsafe class C
{
    System.Collections.Generic.IEnumerable<int> M()
    {
        yield return 1;
        local();
        void local()
        {
            int* p = null; // error: unsafe code in safe context
        }
    }
}
```

**Fix:** Add the `unsafe` modifier to the local function:
```csharp
unsafe void local()
{
    int* p = null; // OK
}
```

### Collection expression overload resolution changes

**Impact: Low–Medium.** Two changes in how collection expressions resolve overloads:

1. **Empty collection expressions no longer use span to tiebreak**: When passing `[]` to an overloaded method without a clear element type, `ReadOnlySpan<T>` vs `Span<T>` is no longer used to disambiguate. This can turn previously successful calls into errors.

```csharp
class C
{
    static void M(ReadOnlySpan<int> ros) {}
    static void M(Span<object> s) {}

    static void Main()
    {
        M([]); // Chose ReadOnlySpan<int> in C# 12, error in C# 13
    }
}
```

2. **Exact element type is now preferred**: Overload resolution prefers an exact element type match from expressions. This can change which overload is selected:

```csharp
class C
{
    static void M(ReadOnlySpan<byte> ros) {}
    static void M(Span<int> s) {}

    static void Main()
    {
        M([1]); // ReadOnlySpan<byte> in C# 12, Span<int> in C# 13
    }
}
```

**Fix:** Add explicit casts or use a typed variable to select the desired overload.

### Default and params parameters considered in method group natural type

**Impact: Low.** The compiler previously inferred different delegate types depending on candidate order when default parameter values or `params` arrays were used. Now an ambiguity error is emitted. Fix by using explicit delegate types instead of `var`.

### Other low-impact source changes

- **`scoped` in lambda parameters (inherited from C# 12 changes)**: Always treated as a modifier in newer language versions.
- **Indexers without `DefaultMemberAttribute`**: No longer allowed (CS0656).
- **`dotnet_style_require_accessibility_modifiers`** now enforces on interface members consistently.
