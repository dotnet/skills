---
name: debugging-memory-leaks
description: Diagnose and fix memory leaks in .NET applications using diagnostic tools, heap analysis, and GC root tracing. Use when a .NET application shows increasing memory usage, OutOfMemoryException, or high GC pressure.
---

# Debugging Memory Leaks

## When to Use

- Application memory grows continuously over time
- `OutOfMemoryException` is thrown
- GC Gen 2 collections are frequent and memory doesn't decrease
- Container or process gets OOM-killed

## When Not to Use

- Memory usage is high but stable (may be normal working set)
- The issue is high CPU, not memory (use `profiling-dotnet-apps` instead)
- The application is not .NET

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Running process or dump file | Yes | PID of a live process or path to a `.dmp` file |
| Symptom description | No | When memory grows, how fast, any trigger patterns |

## Workflow

### Step 1: Confirm memory is actually leaking

```bash
dotnet-counters monitor --process-id <PID> --counters System.Runtime[gc-heap-size,gen-2-gc-count,gen-0-gc-count]
```

Watch for 30-60 seconds. A leak looks like:

- `gc-heap-size` steadily increases
- `gen-2-gc-count` climbs frequently
- Memory does not drop after Gen 2 collections

If `gc-heap-size` is stable, the issue may be native memory (not covered by this skill).

### Step 2: Capture heap snapshots

Take two snapshots with a time gap to see what's growing.

**Snapshot 1:**

```bash
dotnet-dump collect --process-id <PID> --output snapshot1.dmp
```

Wait for the suspected leak to accumulate (e.g., 2-5 minutes under load).

**Snapshot 2:**

```bash
dotnet-dump collect --process-id <PID> --output snapshot2.dmp
```

### Step 3: Analyze the heap

Open the second (larger) snapshot:

```bash
dotnet-dump analyze snapshot2.dmp
```

Run the heap statistics command:

```
dumpheap -stat
```

This outputs a table sorted by total size. The bottom rows are the largest types. Look for:

- Types with unexpectedly high counts
- Types you recognize from the application code
- Event handler delegates, closure types, or `byte[]` arrays

### Step 4: Inspect the suspicious type

```
dumpheap -type <FullTypeName>
```

Pick an object address from the output and trace why it's alive:

```
gcroot <address>
```

This shows the reference chain from GC roots to the object. Common leak patterns:

| Root Pattern | Likely Cause | Fix |
|---|---|---|
| Static field → collection → objects | Static collection grows unbounded | Limit size or use `ConditionalWeakTable` |
| Event handler delegate chain | Event subscriptions not removed | Unsubscribe in `Dispose()` |
| `ConcurrentDictionary` → many entries | Cache without eviction | Add TTL or size limit, use `MemoryCache` |
| `HttpClient` → `SocketsHttpHandler` | Creating new `HttpClient` per request | Use `IHttpClientFactory` |
| Timer callback → captured closure | Timer prevents GC of captured objects | Dispose timers, use weak references |

### Step 5: Verify the fix

After applying the fix:

1. Restart the application
2. Run `dotnet-counters monitor` again
3. Verify `gc-heap-size` stabilizes under the same workload
4. Optionally take new snapshots to confirm the leaking type count is stable

## Quick Reference: Common .NET Memory Leak Sources

```
Leak                          → Fix
────────────────────────────────────────────────────────
Static event handlers         → Unsubscribe or use weak events
Captured closures in timers   → Dispose timers properly
Unbounded caches              → Use MemoryCache with size limits
HttpClient per-request        → Use IHttpClientFactory
String concatenation in loops → Use StringBuilder
Large arrays pinned by GC     → Use ArrayPool<T>
IDisposable not disposed      → Implement dispose pattern, use 'using'
```

## Validation

- [ ] `dotnet-counters` confirms memory growth pattern
- [ ] Heap dump collected successfully
- [ ] `dumpheap -stat` identifies the growing type
- [ ] `gcroot` traces the retention path
- [ ] Fix applied and memory stabilizes under repeated load

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Dump collection freezes the process | Warn user; dumps pause the app briefly |
| Heap shows `System.Byte[]` as top consumer | Trace individual arrays with `gcroot` to find the real owner |
| Native memory leak (RSS grows but GC heap is flat) | Use OS tools (`vmmap`, `/proc/<pid>/maps`); not a managed leak |
| Gen 0/1 objects dominating | Those are short-lived; focus on Gen 2 for leaks |
| `gcroot` shows no root | Object is likely already eligible for collection; pick a different instance |
