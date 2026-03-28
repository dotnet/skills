# Structural Patterns

Patterns detected by the **absence** of a keyword or interface. These require codebase-wide counting scans, not single-file matching.

### Seal Classes for Devirtualization
🟡 **DO** seal all leaf classes (those not subclassed) | .NET Core 3.0+

Sealing lets the JIT devirtualize/inline virtual calls and use pointer comparison for type checks. Every non-abstract, non-static class that is not subclassed should be sealed.

**Detection:** This is an absence pattern — scan for classes that are NOT sealed.

```bash
# Count unsealed (non-abstract, non-static) classes
grep -rn --include='*.cs' -E '^\s*((public|internal|private|protected|file)\s+)?(partial\s+)?class ' --exclude-dir=bin --exclude-dir=obj . | grep -v 'sealed' | grep -v 'abstract' | grep -v 'static' | wc -l

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
