# Helper Extension Methods

Add these to the project when needed during migration. The `WhereNotNull` pattern is used in the Roslyn compiler codebase itself. Do not use these if they would introduce Linq to the codebase for the first time.

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

internal static class NullableExtensions
{
    /// <summary>
    /// Filters null values from a sequence and narrows the type from T? to T.
    /// Unlike .Where(x => x != null), this gives the compiler correct type information.
    /// </summary>
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source) where T : class
        => source.Where(x => x is not null)!;

    /// <summary>
    /// Filters null values from a sequence of nullable value types and unwraps to T.
    /// </summary>
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source) where T : struct
        => source.Where(x => x.HasValue).Select(x => x!.Value);
}
```
