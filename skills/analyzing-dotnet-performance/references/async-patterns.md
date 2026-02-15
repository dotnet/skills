# Async & Concurrency Patterns

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
# async void methods (correctness issue — crashes on exception)
grep -rn --include='*.cs' 'async void' --exclude-dir=bin --exclude-dir=obj . | grep -v 'event' | wc -l
```

### Patterns Requiring Manual Review

- **Sync-over-async** (`.Result`, `.Wait()`): `.Result` matches any property named Result — needs type context to confirm it's `Task.Result`
