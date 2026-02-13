# Structural Patterns

Patterns detected by the **absence** of a keyword or interface. These require codebase-wide counting scans, not single-file matching.

### Seal Classes for Devirtualization
🟡 **DO** seal all leaf classes (those not subclassed) | .NET Core 3.0+

Sealing lets the JIT devirtualize/inline virtual calls and use pointer comparison for type checks. Every non-abstract, non-static class that is not subclassed should be sealed.

**Detection:** This is an absence pattern — scan for classes that are NOT sealed.

```bash
# Count unsealed (non-abstract, non-static) classes
grep -rn --include='*.cs' -E '^\s*(public |internal |private )?(partial )?class ' --exclude-dir=bin --exclude-dir=obj . | grep -v 'sealed' | grep -v 'abstract' | grep -v 'static' | wc -l

# Count already-sealed classes (verify the inverse)
grep -rn --include='*.cs' 'sealed class' --exclude-dir=bin --exclude-dir=obj . | wc -l
```

**Exclusions:** Do not seal classes that are subclassed elsewhere in the codebase. Identifying base classes requires manual review — grep for `: ClassName` patterns and cross-reference, but expect false positives from interface implementations and generic constraints.

❌
```csharp
internal class MyHandler : Base
{ public override int Run() => 42; }
```
✅
```csharp
internal sealed class MyHandler : Base
{ public override int Run() => 42; }
```

**Impact: Virtual calls up to 500x faster; type checks ~25x faster. Severity scales with count.**

**Scale-based severity:**
- 1-10 unsealed leaf classes → ℹ️ Info
- 11-50 unsealed leaf classes → 🟡 Moderate
- 50+ unsealed leaf classes → 🟡 Moderate (elevated priority)

### Implement IEquatable\<T\> on Structs to Avoid Boxing
🟡 **DO** implement `IEquatable<T>` on all structs used in collections or comparisons | .NET Core+

**Detection:** Scan for structs that do NOT implement `IEquatable<T>`.

```bash
# Count structs without IEquatable
grep -rn --include='*.cs' -E '^\s*(public |internal |private )?(readonly )?struct ' --exclude-dir=bin --exclude-dir=obj . | grep -v 'IEquatable' | wc -l

# Count structs with IEquatable (verify the inverse)
grep -rn --include='*.cs' -E 'struct .+IEquatable' --exclude-dir=bin --exclude-dir=obj . | wc -l
```

❌
```csharp
public struct Point
{
    public int X, Y;
}
```
✅
```csharp
public struct Point : IEquatable<Point>
{
    public int X, Y;
    public bool Equals(Point other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is Point p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(X, Y);
}
```

**Impact: ~2.5x faster equality checks, eliminates boxing allocations in Dictionary/HashSet lookups.**

**Scale-based severity:**
- 1-5 structs without IEquatable → ℹ️ Info
- 6+ structs without IEquatable that are used in Dictionary/HashSet → 🟡 Moderate
