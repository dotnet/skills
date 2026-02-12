---
name: appdomain-migration
description: Guides migration of .NET Framework AppDomain usage to modern .NET alternatives. Use when modernizing code that relies on AppDomain for plugin isolation, dynamic assembly loading, sandboxing, or configuration isolation.
---

# AppDomain Migration

This skill helps an agent migrate .NET Framework code that uses `System.AppDomain` to the appropriate modern .NET (6+/8+) replacement. Because there is no single direct replacement for AppDomains, the skill identifies the usage pattern first, then applies the correct migration strategy.

## When to Use

- Migrating a .NET Framework project to .NET 6+ that uses `AppDomain.CreateDomain`
- Replacing `AppDomain`-based plugin or add-in hosting with `AssemblyLoadContext`
- Removing `MarshalByRefObject` cross-domain communication patterns
- Converting dynamic assembly loading that depends on AppDomain isolation
- Replacing AppDomain-based sandboxing with process-level isolation
- Resolving build errors related to removed AppDomain APIs after a target framework change

## When Not to Use

- The code only uses `AppDomain.CurrentDomain` for event subscriptions like `UnhandledException` or `AssemblyResolve` (these still work in modern .NET; no migration needed)
- The project will remain on .NET Framework indefinitely
- The AppDomain usage is inside a third-party library you do not control

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Source project or solution | Yes | The .NET Framework project containing AppDomain usage |
| Target framework | Yes | The modern .NET version to target (e.g., `net8.0`) |
| AppDomain usage locations | Recommended | Files or classes that reference `AppDomain.CreateDomain`, `MarshalByRefObject`, or cross-domain delegates |

## Workflow

### Step 1: Inventory AppDomain usage

Search the codebase for all AppDomain-related APIs:

- `AppDomain.CreateDomain`
- `AppDomain.Unload`
- `AppDomain.CurrentDomain.Load`
- `MarshalByRefObject` subclasses
- `CrossAppDomainDelegate`
- `AppDomainSetup`
- `[Serializable]` types used for cross-domain data transfer
- `DoCallBack` invocations
- `SetData` / `GetData` for cross-domain state

Record each usage location, the pattern it represents, and any cross-domain types involved.

### Step 2: Classify each usage pattern

Categorize every usage into one of the following patterns:

| Pattern | Description | Modern replacement |
|---------|-------------|--------------------|
| **Plugin isolation** | Loading and unloading third-party assemblies in an isolated domain | `AssemblyLoadContext` with `isCollectible: true` |
| **Dynamic assembly loading** | Loading assemblies by path or name at runtime without isolation requirements | `AssemblyLoadContext.Default.LoadFromAssemblyPath` or `Assembly.LoadFrom` |
| **Sandboxing / partial trust** | Restricting permissions for untrusted code | Separate process with restricted OS-level permissions |
| **Configuration isolation** | Using per-domain config files via `AppDomainSetup.ConfigurationFile` | `IConfiguration` with per-component config sources |
| **Unloadability** | Loading code that must be unloaded to free memory or update in place | Collectible `AssemblyLoadContext` |
| **Cross-domain remoting** | Using `MarshalByRefObject` proxies to call across domains | In-process interfaces across `AssemblyLoadContext` boundaries, or out-of-process communication (named pipes, gRPC) |

If a single AppDomain serves multiple purposes, list all patterns and address each one.

### Step 3: Migrate plugin isolation and unloadability

For code that creates an AppDomain to load and later unload plugins:

1. Create a custom `AssemblyLoadContext` subclass with `isCollectible: true`:

```csharp
public class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string? assemblyPath = _resolver.ResolveAssemblyPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = _resolver.ResolveUnmanagedDllPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }
        return IntPtr.Zero;
    }
}
```

2. Replace `AppDomain.CreateDomain` + `CreateInstanceAndUnwrap` with loading into the custom context:

```csharp
var context = new PluginLoadContext(pluginPath);
var assembly = context.LoadFromAssemblyPath(pluginPath);
var type = assembly.GetType("MyPlugin.PluginClass");
var instance = Activator.CreateInstance(type!);
```

3. Replace `AppDomain.Unload(domain)` with unloading the context:

```csharp
context.Unload();
```

4. Ensure no references to types loaded in the context are held after `Unload()`, otherwise the context will not be garbage collected. Use `WeakReference` to verify collectibility during testing.

### Step 4: Migrate MarshalByRefObject cross-domain communication

`MarshalByRefObject` has no equivalent across `AssemblyLoadContext` boundaries. Choose a replacement based on isolation needs:

**If the code stays in-process (same `AssemblyLoadContext` boundary):**

1. Define a shared interface in an assembly loaded by the default context.
2. Have the plugin implement that interface.
3. Cast the loaded type to the shared interface instead of using `MarshalByRefObject` proxies.

```csharp
// Shared contract (loaded in default context)
public interface IPlugin
{
    string Execute(string input);
}

// In plugin (loaded in PluginLoadContext)
public class MyPlugin : IPlugin
{
    public string Execute(string input) => $"Processed: {input}";
}

// Host code
var instance = (IPlugin)Activator.CreateInstance(type!);
var result = instance.Execute("hello");
```

**If strong isolation is required (untrusted code):**

1. Move the plugin to a separate process.
2. Communicate via named pipes, gRPC, or another IPC mechanism.
3. The host process starts and manages the plugin process lifecycle.

### Step 5: Migrate sandboxing and partial trust

Modern .NET does not support Code Access Security (CAS) or partial trust. Replace with:

1. Run untrusted code in a **separate process** with restricted OS permissions.
2. On Windows, use Job Objects or a restricted user account.
3. On Linux, use containers, seccomp, or AppArmor profiles.
4. Communicate with the sandboxed process via IPC (named pipes, gRPC, Unix domain sockets).

Remove all `PermissionSet`, `SecurityPermission`, and `AppDomain.SetAppDomainPolicy` calls — they have no effect in modern .NET.

### Step 6: Migrate configuration isolation

Replace `AppDomainSetup.ConfigurationFile` with the modern configuration system:

1. Add `Microsoft.Extensions.Configuration` packages if not already present.
2. Create a per-component `IConfiguration` instance:

```csharp
var config = new ConfigurationBuilder()
    .SetBasePath(pluginDirectory)
    .AddJsonFile("pluginsettings.json", optional: true)
    .Build();
```

3. Remove `AppDomainSetup` configuration and any `ConfigurationManager` calls that relied on per-domain config files.

### Step 7: Clean up removed APIs

After migrating all patterns, remove or replace any remaining references:

| Removed API | Replacement |
|-------------|-------------|
| `AppDomain.CreateDomain` | `new PluginLoadContext(path)` |
| `AppDomain.Unload` | `AssemblyLoadContext.Unload()` |
| `AppDomain.CurrentDomain.Load(byte[])` | `AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(bytes))` |
| `AppDomain.SetData` / `GetData` | Static state, dependency injection, or `AsyncLocal<T>` |
| `AppDomain.DoCallBack` | Direct method call or IPC |
| `MarshalByRefObject` | Shared interface or IPC |
| `[Serializable]` for cross-domain transfer | Shared types in a common assembly, or DTO serialization over IPC |

### Step 8: Verify the migration

1. Build the project targeting the new framework. Confirm zero `AppDomain`-related compile errors.
2. Run existing tests. If tests created AppDomains for isolation, update them to use a custom `AssemblyLoadContext` with `isCollectible: true`.
3. For unloadability scenarios, add a test that:
   - Loads an assembly into a collectible `AssemblyLoadContext`
   - Unloads the context
   - Uses a `WeakReference` to confirm the context was garbage collected
4. For plugin scenarios, verify that plugins load, execute, and unload without memory leaks.
5. Search the codebase for any remaining references to `AppDomain.CreateDomain`, `MarshalByRefObject`, or `CrossAppDomainDelegate`.

## Validation

- [ ] No references to `AppDomain.CreateDomain` remain in the migrated code
- [ ] No `MarshalByRefObject` subclasses remain (unless still targeting .NET Framework in a multi-target build)
- [ ] Project builds cleanly against the target framework with no AppDomain-related errors
- [ ] Plugin load/unload scenarios work correctly with `AssemblyLoadContext`
- [ ] If collectible contexts are used, a `WeakReference` test confirms unloading works
- [ ] Cross-domain communication replaced with shared interfaces or IPC
- [ ] No CAS or partial trust APIs remain (`PermissionSet`, `SecurityPermission`, etc.)
- [ ] Existing tests pass or have been updated for the new isolation model

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Holding references to types from an unloaded context prevents GC | Ensure all references (including event handlers) are released before calling `Unload()`. Use `WeakReference` to verify. |
| Shared types loaded in both default and plugin contexts cause `InvalidCastException` | Load shared interface assemblies only in the default context. Use `AssemblyDependencyResolver` and override `Load` to return `null` for shared assemblies so they fall through to the default context. |
| Assuming `AssemblyLoadContext` provides security isolation | It does not. `AssemblyLoadContext` provides assembly isolation, not permission isolation. Use process boundaries for security. |
| Removing `[Serializable]` from types still used by other serializers | Only remove `[Serializable]` if it was solely for cross-AppDomain marshaling. Check for `BinaryFormatter`, remoting, or other serialization usage first. |
| Using `Assembly.LoadFrom` instead of `AssemblyLoadContext.LoadFromAssemblyPath` | `LoadFrom` loads into the default context and does not provide isolation. Always use a custom `AssemblyLoadContext` when isolation is needed. |
| Forgetting to handle unmanaged (native) DLL loading in the plugin context | Override `LoadUnmanagedDll` in your custom `AssemblyLoadContext` to resolve native dependencies from the plugin directory. |
