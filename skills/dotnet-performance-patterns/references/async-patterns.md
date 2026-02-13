# Async & Concurrency Patterns

### ConfigureAwait(false) in Library Code
🟡 **DO** use `ConfigureAwait(false)` on every `await` in library code | .NET Core+

❌
```csharp
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

### Don't Expose Async Wrappers for Sync Methods
🟡 **AVOID** wrapping sync methods with `Task.Run` in libraries | .NET Core+

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

### Don't Expose Sync Wrappers for Async Methods
🟡 **AVOID** creating sync wrappers that block on async implementations | .NET Core+

❌
```csharp
public string GetData() => GetDataAsync().Result;
```
✅
```csharp
public async Task<string> GetDataAsync() { /* ... */ }
```

**Impact: Prevents deadlocks and thread pool starvation from hidden sync-over-async blocking.**

### Use ValueTask for Hot Paths with Frequent Sync Completion
🟡 **DO** use `ValueTask<T>` on hot paths where sync completion is common | .NET Core 2.1+

❌
```csharp
public async Task<int> ReadAsync(Memory<byte> buffer)
{
    if (_bufferedCount > 0)
        return ReadFromBuffer(buffer.Span);
    return await ReadAsyncCore(buffer);
}
```
✅
```csharp
public ValueTask<int> ReadAsync(Memory<byte> buffer)
{
    if (_bufferedCount > 0)
        return new ValueTask<int>(ReadFromBuffer(buffer.Span));
    return new ValueTask<int>(ReadAsyncCore(buffer));
}
```

**Impact: Eliminates Task\<T\> allocation on synchronous completion — the struct stores results inline.**

### Use Parallel.ForEachAsync for Async Parallel Processing
🟡 **DO** use `Parallel.ForEachAsync` for controlled async parallelism | .NET 6+

❌
```csharp
var tasks = items.Select(async item => await ProcessAsync(item));
await Task.WhenAll(tasks);
```
✅
```csharp
await Parallel.ForEachAsync(items,
    new ParallelOptions { MaxDegreeOfParallelism = 10 },
    async (item, ct) => await ProcessAsync(item));
```

**Impact: Safer, more controllable parallel execution with configurable throttling.**

### Use Channels for Producer/Consumer
🟡 **DO** use `System.Threading.Channels` for producer-consumer patterns | .NET Core 3.0+

❌
```csharp
var queue = new BlockingCollection<WorkItem>();
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

### Avoid Async Lambda with Action-Accepting Methods
🟡 **AVOID** passing async lambdas to methods expecting `Action` | .NET Core+

❌
```csharp
Time(async () => await Task.Delay(10_000));
```
✅
```csharp
static async Task TimeAsync(Func<Task> action) { await action(); }
await TimeAsync(async () => await Task.Delay(10_000));
```

**Impact: Correctness issue — unobserved exceptions in async void crash the process.**

### Avoid False Sharing with Thread-Local State
🟡 **AVOID** adjacent mutable fields written by different threads | .NET 7+

❌
```csharp
class SharedCounters
{
    public long Counter1;
    public long Counter2;
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

## Detection

Scan recipes for async anti-patterns. Run these and report exact counts.

```bash
# async void methods (crashes on exception) — critical-patterns.md #1
grep -rn --include='*.cs' 'async void' --exclude-dir=bin --exclude-dir=obj . | grep -v 'event' | wc -l
```

### Patterns Requiring Manual Review

- **Sync-over-async** (`.Result`, `.Wait()`): `.Result` matches any property named Result — needs type context to confirm it's `Task.Result`
