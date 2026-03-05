# Extensions and Hosting Breaking Changes (.NET 10)

These changes affect projects using `Microsoft.Extensions.Hosting`, `BackgroundService`, `Microsoft.Extensions.Configuration`, and related libraries.

## Behavioral Changes

### BackgroundService.ExecuteAsync runs entirely on a background thread

**Impact: Medium.** Previously, the synchronous code in `ExecuteAsync` before the first `await` ran on the main thread during startup, blocking other hosted services from starting. Now ALL of `ExecuteAsync` runs on a background thread.

**If startup ordering matters:**

```csharp
// Option 1: Move synchronous startup code to StartAsync
public override async Task StartAsync(CancellationToken cancellationToken)
{
    // This still runs synchronously during startup
    InitializeResources();
    await base.StartAsync(cancellationToken);
}

// Option 2: Use IHostedLifecycleService for fine-grained lifecycle control
public class MyService : BackgroundService, IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken ct)
    {
        // runs before StartAsync
        return Task.CompletedTask;
    }

    public Task StartedAsync(CancellationToken ct)
    {
        // runs after StartAsync
        return Task.CompletedTask;
    }
    // ... other lifecycle methods
}

// Option 3: Move code to the constructor
public MyService(ILogger<MyService> logger)
{
    // Constructor code runs during DI resolution
}
```

### Null values preserved in configuration

**Impact: Medium.** JSON `null` values are now properly bound instead of being converted to empty strings or ignored. **Empty arrays (`[]`) are also now correctly bound as empty arrays instead of being ignored.**

| Scenario | .NET 9 behavior | .NET 10 behavior |
|----------|----------------|-----------------|
| `"StringProperty": null` | Bound as `""` (empty string) | Bound as `null` (overwrites constructor default) |
| `"IntProperty": null` | Ignored (kept constructor default) | Bound as `null` (if `int?`) |
| `"Array": [null, null]` | Bound as `["", ""]` | Bound as `[null, null]` |
| **`"Array": []`** | **Ignored (`null`)** | **Bound as empty array `[]`** |

**If you need the old behavior:**
- Replace `null` with `""` in JSON config files
- Or remove `null` entries to skip binding

### Fix issues in GetKeyedService() and GetKeyedServices() with AnyKey

`GetKeyedService()` and `GetKeyedServices()` with `AnyKey` now work correctly. If your code relied on the previous buggy behavior, update it.

### Message no longer duplicated in Console log output

When using the JSON console logger, messages are no longer duplicated. If your log parsing relied on the duplicated format, update it.

### ProviderAliasAttribute moved to Microsoft.Extensions.Logging.Abstractions assembly

`ProviderAliasAttribute` has moved assemblies. If you reference it directly by assembly-qualified name, update the reference. Source-incompatible for code using assembly-qualified type references.

### Removed DynamicallyAccessedMembers annotation from trim-unsafe configuration code

The `[DynamicallyAccessedMembers]` annotation was removed from certain configuration APIs that are not trim-safe. Binary incompatible for code that relied on the annotation for trimming analysis.
