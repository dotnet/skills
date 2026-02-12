# Async & Concurrency Patterns

Moderate-priority patterns for async/await, parallel processing, and thread synchronization. These improve scalability and reduce overhead in concurrent .NET code.

### ConfigureAwait(false) in Library Code
🟡 **DO** use `ConfigureAwait(false)` on every `await` in library code | .NET Core+

Avoids capturing the caller's `SynchronizationContext`, preventing deadlocks when consumers block on returned tasks. Also eliminates unnecessary thread marshaling overhead.

❌
```csharp
// Library code — risks deadlock if caller calls .Result
public async Task<string> GetDataAsync(string url)
{
    var response = await _httpClient.GetAsync(url);
    return await response.Content.ReadAsStringAsync();
}
```
✅
```csharp
public async Task<string> GetDataAsync(string url)
{
    var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
}
```

**Impact: Avoids SynchronizationContext.Post overhead and prevents deadlocks when consumers call .Result or .Wait().**

---

### Don't Expose Async Wrappers for Sync Methods
🟡 **AVOID** wrapping sync methods with `Task.Run` in libraries | .NET Core+

Fake async wrappers add thread pool queue/dequeue overhead without scalability benefit. Let the consumer decide whether to offload CPU-bound work.

❌
```csharp
public Task<int> ComputeHashAsync(byte[] data) =>
    Task.Run(() => ComputeHash(data));
```
✅
```csharp
public int ComputeHash(byte[] data) { /* CPU-bound work */ }
// Consumer decides: var hash = await Task.Run(() => lib.ComputeHash(data));
```

**Impact: Eliminates unnecessary thread pool queue/dequeue overhead per call.**

---

### Don't Expose Sync Wrappers for Async Methods
🟡 **AVOID** creating sync wrappers that block on async implementations | .NET Core+

Blocking on async code with `.Result` hides the true async nature of the API and risks deadlocks. Expose only the async method and let callers propagate async.

❌
```csharp
public string GetData() => GetDataAsync().Result; // DANGEROUS
```
✅
```csharp
public async Task<string> GetDataAsync() { /* ... */ }
```

**Impact: Prevents deadlocks and thread pool starvation from hidden sync-over-async blocking.**

---

### Use ValueTask for Hot Paths with Frequent Sync Completion
🟡 **DO** use `ValueTask<T>` on hot paths where sync completion is common | .NET Core 2.1+

`ValueTask<T>` avoids the `Task<T>` heap allocation when the result is immediately available. Only use on proven hot paths — `Task<T>` is safer and simpler for general APIs.

❌
```csharp
public async Task<int> ReadAsync(Memory<byte> buffer)
{
    if (_bufferedCount > 0)
        return ReadFromBuffer(buffer.Span); // allocates Task<int>
    return await ReadAsyncCore(buffer);
}
```
✅
```csharp
public ValueTask<int> ReadAsync(Memory<byte> buffer)
{
    if (_bufferedCount > 0)
        return new ValueTask<int>(ReadFromBuffer(buffer.Span)); // no alloc
    return new ValueTask<int>(ReadAsyncCore(buffer));
}
```

**Impact: Eliminates Task\<T\> allocation on synchronous completion — the struct stores results inline.**

---

### Use Parallel.ForEachAsync for Async Parallel Processing
🟡 **DO** use `Parallel.ForEachAsync` for controlled async parallelism | .NET 6+

Replaces hand-rolled `Task.WhenAll` + `Select` patterns with built-in, configurable parallel async execution. Prevents unbounded concurrency and thread pool starvation.

❌
```csharp
var tasks = items.Select(async item => await ProcessAsync(item));
await Task.WhenAll(tasks); // unbounded concurrency
```
✅
```csharp
await Parallel.ForEachAsync(items,
    new ParallelOptions { MaxDegreeOfParallelism = 10 },
    async (item, ct) => await ProcessAsync(item));
```

**Impact: Safer, more controllable parallel execution with configurable throttling.**

---

### Use Channels for Producer/Consumer
🟡 **DO** use `System.Threading.Channels` for producer-consumer patterns | .NET Core 3.0+

Channels provide optimized producer-consumer with allocation-free async reads via `IValueTaskSource`. Dramatically more efficient than `BlockingCollection<T>` or manual concurrent queue patterns.

❌
```csharp
var queue = new BlockingCollection<WorkItem>();
// Blocks threads waiting for items
var item = queue.Take();
```
✅
```csharp
var channel = Channel.CreateUnbounded<WorkItem>();

// Producer
await channel.Writer.WriteAsync(item);

// Consumer
await foreach (var item in channel.Reader.ReadAllAsync())
    Process(item);
```

**Impact: ~25% faster, ~95% fewer GC collections vs manual approaches.**

---

### Avoid Async Lambda with Action-Accepting Methods
🟡 **AVOID** passing async lambdas to methods expecting `Action` | .NET Core+

When an async lambda is passed to a method accepting `Action` (not `Func<Task>`), it becomes `async void`. The caller cannot observe completion or exceptions — fire-and-forget may crash the process.

❌
```csharp
// Inferred as async void — Time() returns immediately!
Time(async () => await Task.Delay(10_000));
```
✅
```csharp
static async Task TimeAsync(Func<Task> action) { await action(); }
await TimeAsync(async () => await Task.Delay(10_000));
```

**Impact: Correctness issue — unobserved exceptions in async void crash the process.**

---

### Avoid False Sharing with Thread-Local State
🟡 **AVOID** adjacent mutable fields written by different threads | .NET 7+

Shared mutable data on adjacent cache lines causes false sharing — cross-core cache invalidation that can reduce throughput by 10x+ on multi-core workloads. Use padding or `[ThreadStatic]` to isolate per-thread state.

❌
```csharp
class SharedCounters
{
    public long Counter1; // Thread 1 writes
    public long Counter2; // Thread 2 writes — same cache line!
}
```
✅
```csharp
[StructLayout(LayoutKind.Explicit, Size = 128)]
struct PaddedCounter
{
    [FieldOffset(0)] public long Value;
}
```

**Impact: Eliminates cross-core cache invalidation — can improve multi-threaded throughput by 10x+.**

---

### Use Interlocked Operations for Atomic Updates
🟡 **DO** use `Interlocked` for simple atomic operations on shared variables | .NET Core+

`Interlocked.Increment`, `CompareExchange`, etc. are lock-free JIT intrinsics — dramatically faster than taking a lock for simple counter or flag updates.

❌
```csharp
lock (_lock) { _count++; }
```
✅
```csharp
Interlocked.Increment(ref _count);
```

**Impact: Lock-free single CPU instruction vs full lock acquisition/release overhead.**

---

### Use SemaphoreSlim for Async Synchronization
🟡 **DO** use `SemaphoreSlim.WaitAsync()` instead of `lock` in async code | .NET Core+

You cannot `await` inside `lock`, and `Monitor.Enter/Exit` across async boundaries fails because exit may run on a different thread. `SemaphoreSlim` is lightweight and doesn't block threads while waiting.

❌
```csharp
lock (_syncObj)
{
    await DoSomethingAsync(); // CS1996 compile error
}
```
✅
```csharp
private readonly SemaphoreSlim _semaphore = new(1, 1);

await _semaphore.WaitAsync();
try { await DoSomethingAsync(); }
finally { _semaphore.Release(); }
```

**Impact: Enables async-compatible synchronization without thread pool starvation or deadlocks.**
