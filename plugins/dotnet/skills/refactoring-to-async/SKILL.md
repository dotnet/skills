---
name: refactoring-to-async
description: >
  Convert synchronous .NET code to async/await with proper Task propagation,
  CancellationToken support, and async anti-pattern removal.
  USE FOR: converting blocking I/O to async, fixing sync-over-async patterns.
  DO NOT USE FOR: CPU-bound computation, code with no I/O.
---

# Refactoring to Async

> **Note:** All code examples below are for ASP.NET Core / application code.
> In **library code**, add `.ConfigureAwait(false)` to every `await`.

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Code to refactor | Yes | The synchronous methods to convert |
| Scope | No | Single method, class, or full call chain |

## Workflow

### Step 1: Identify blocking I/O calls

Look for these synchronous I/O methods and blocking patterns in the codebase:

- `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` — sync-over-async blocking
- `stream.Read`, `stream.Write`, `reader.ReadToEnd` — synchronous stream I/O
- `File.ReadAllText`, `File.WriteAllBytes`, `File.ReadAllLines` — synchronous file I/O
- `connection.Open`, `command.ExecuteReader`, `command.ExecuteNonQuery` — synchronous database calls
- `HttpClient.Send`, `WebClient.Download*` — synchronous HTTP calls
- `Thread.Sleep` — thread-blocking delay

Common blocking patterns to convert:

| Synchronous | Async Replacement |
|---|---|
| `stream.Read(buffer, offset, count)` | `await stream.ReadAsync(buffer, offset, count, ct)` |
| `stream.Write(data, offset, count)` | `await stream.WriteAsync(data, offset, count, ct)` |
| `reader.ReadToEnd()` | `await reader.ReadToEndAsync()` |
| `File.ReadAllText(path)` | `await File.ReadAllTextAsync(path, ct)` |
| `File.WriteAllBytes(...)` | `await File.WriteAllBytesAsync(..., ct)` |
| `client.Send(request)` | `await client.SendAsync(request, ct)` |
| `connection.Open()` | `await connection.OpenAsync(ct)` |
| `command.ExecuteReader()` | `await command.ExecuteReaderAsync(ct)` |
| `Thread.Sleep(ms)` | `await Task.Delay(ms, ct)` |
| `task.Result` | `await task` |
| `task.Wait()` | `await task` |

### Step 2: Convert bottom-up

Start from the lowest-level I/O calls and work upward through the call chain. This avoids sync-over-async wrappers.

**Before:**

```csharp
public string GetUserData(int userId)
{
    var response = _httpClient.Send(new HttpRequestMessage(HttpMethod.Get, $"/users/{userId}"));
    var body = new StreamReader(response.Content.ReadAsStream()).ReadToEnd();
    return body;
}
```

**After:**

```csharp
public async Task<string> GetUserDataAsync(int userId, CancellationToken ct = default)
{
    using var response = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/users/{userId}"), ct);
    return await response.Content.ReadAsStringAsync(ct);
}
```

### Step 3: Propagate async through the call chain

Every caller of an async method must also become async. Follow the chain upward:

```csharp
// Layer 1: Data access (already converted)
public async Task<User> GetUserAsync(int id, CancellationToken ct) { ... }

// Layer 2: Business logic (convert next)
public async Task<UserDto> GetUserProfileAsync(int id, CancellationToken ct)
{
    var user = await GetUserAsync(id, ct);
    return MapToDto(user);  // sync mapping is fine
}

// Layer 3: API endpoint (convert last)
app.MapGet("/users/{id}", async (int id, CancellationToken ct, IUserService svc) =>
    await svc.GetUserProfileAsync(id, ct));
```

### Step 4: Add CancellationToken support

Accept `CancellationToken` as the last parameter in every async method and pass it through:

```csharp
public async Task<List<Order>> GetOrdersAsync(
    int userId,
    CancellationToken ct = default)   // Always provide a default
{
    using var response = await _client.GetAsync($"/orders?user={userId}", ct);
    var json = await response.Content.ReadAsStringAsync(ct);
    return JsonSerializer.Deserialize<List<Order>>(json) ?? [];
}
```

ASP.NET Core automatically supplies a `CancellationToken` that fires when the client disconnects.

### Step 5: Convert disposables to async

Types that implement `IAsyncDisposable` (e.g., `SqlConnection`, `DbContext`, `Stream`) should use `await using` instead of `using`:

```csharp
// Before
using var connection = new SqlConnection(connectionString);

// After
await using var connection = new SqlConnection(connectionString);
```

### Step 6: Update interfaces

```csharp
// Before
public interface IUserRepository
{
    User GetById(int id);
    List<User> GetAll();
}

// After
public interface IUserRepository
{
    Task<User> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<User>> GetAllAsync(CancellationToken ct = default);
}
```

### Step 7: Build and fix

```bash
dotnet build
```

Common errors after async refactoring:

| Error | Fix |
|---|---|
| `CS4032`: `await` in non-async method | Add `async` to the method signature and return `Task` or `Task<T>` |
| `CS0029`: Cannot convert `Task<T>` to `T` | Add `await` before the call |
| `CS0127`: Method returns `Task` but body returns value | Change return type to `Task<T>` |
| `CS1998`: Async method lacks `await` | Remove `async` if no awaits are needed, or the method is genuinely sync |

### Step 8: Verify no anti-patterns remain

Search for remaining `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` calls in the refactored code paths. There should be none.

## Additional Async Patterns

### `IAsyncEnumerable<T>` / `await foreach`

When a method returns `IEnumerable<T>` and each element involves I/O, convert to `IAsyncEnumerable<T>`:

```csharp
// Before
public IEnumerable<User> GetUsers() { /* yields with sync I/O */ }

// After
public async IAsyncEnumerable<User> GetUsersAsync(
    [EnumeratorCancellation] CancellationToken ct = default)
{
    await foreach (var row in db.QueryAsync("SELECT ...", ct))
        yield return MapUser(row);
}
```

### `ValueTask` vs `Task`

Use `Task` by default. Use `ValueTask`/`ValueTask<T>` only for hot-path methods that frequently complete synchronously (e.g., reading from a buffered stream). `ValueTask` limitations: cannot be awaited multiple times, cannot be stored or cached, cannot use `.Result` or `.GetAwaiter().GetResult()` after the first consumption.

## Anti-Patterns to Avoid

| Anti-Pattern | Problem | Correct Approach |
|---|---|---|
| `task.Result` or `task.Wait()` | Blocks thread, risks deadlock | `await task` |
| `async void` methods | Exceptions crash the process | `async Task` (except event handlers) |
| `Task.Run` wrapping async I/O | Wastes a thread pool thread | Call async method directly |
| Missing `ConfigureAwait(false)` in libraries | Can deadlock in WinForms/WPF/ASP.NET sync contexts | Add `.ConfigureAwait(false)` to every `await` in library code; omit in ASP.NET Core app code (no SynchronizationContext) |
| Fire-and-forget without error handling | Swallows exceptions silently | `await` the work, or queue it to a background service (`IHostedService`) with proper error handling |

## Validation

- [ ] `dotnet build` compiles without errors
- [ ] No remaining `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` in converted code
- [ ] `CancellationToken` is propagated through the full call chain
- [ ] `dotnet test` passes (existing tests updated for async)
- [ ] No `async void` methods (except UI event handlers)

## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Deadlock after conversion | Ensure `await` is used everywhere; no `.Result` mixed with `await` |
| Performance worse after conversion | Async adds overhead for CPU-bound work; only use for I/O |
| Forgetting to update tests | Test methods must return `Task` and use `await` |
| Breaking interface consumers | Consider keeping sync wrappers temporarily during staged migration |
| `ValueTask` vs `Task` confusion | Use `Task` by default; `ValueTask` only for hot-path methods that frequently return synchronously |
