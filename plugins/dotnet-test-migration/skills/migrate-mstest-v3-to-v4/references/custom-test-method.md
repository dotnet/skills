# Custom TestMethodAttribute migration

Read this reference when a migration involves custom `TestMethodAttribute`
subclasses or positional display-name constructors. Apply these changes
together to the actual class, preserving its retry and result behavior.

#### 3.1 TestMethodAttribute.Execute -> ExecuteAsync

If you have custom `TestMethodAttribute` subclasses that override `Execute`, change to `ExecuteAsync`. This change was made because the v3 synchronous `Execute` API caused deadlocks when test code used `async`/`await` internally -- the synchronous wrapper would block the thread while the async operation needed that same thread to complete.

```csharp
// Before (v3)
public sealed class MyTestMethodAttribute : TestMethodAttribute
{
    public override TestResult[] Execute(ITestMethod testMethod)
    {
        // custom logic
        return result;
    }
}

// After (v4) -- Option A: wrap synchronous logic with Task.FromResult
public sealed class MyTestMethodAttribute : TestMethodAttribute
{
    public override Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        // custom logic (synchronous)
        return Task.FromResult(result);
    }
}

// After (v4) -- Option B: make properly async
public sealed class MyTestMethodAttribute : TestMethodAttribute
{
    public override async Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        // custom async logic
        return await base.ExecuteAsync(testMethod);
    }
}
```

Use `Task.FromResult` when your override logic is purely synchronous. Use `async`/`await` when you call `base.ExecuteAsync` or other async methods.

#### 3.2 TestMethodAttribute CallerInfo constructor

`TestMethodAttribute` now uses `[CallerFilePath]` and `[CallerLineNumber]` parameters in its constructor.

**If you inherit from TestMethodAttribute**, propagate caller info to the base class:

```csharp
public class MyTestMethodAttribute : TestMethodAttribute
{
    public MyTestMethodAttribute(
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLineNumber = -1)
        : base(callerFilePath, callerLineNumber)
    {
    }
}
```

If the subclass has its own display-name constructor, do not pass that string to
the v4 base constructor. Propagate only caller information and assign the
`DisplayName` property:

```csharp
public sealed class NamedTestMethodAttribute : TestMethodAttribute
{
    public NamedTestMethodAttribute(
        string displayName,
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLineNumber = -1)
        : base(callerFilePath, callerLineNumber)
    {
        DisplayName = displayName;
    }
}
```

**If you use `[TestMethodAttribute("Custom display name")]`**, switch to the named parameter syntax:

```csharp
// Before (v3)
[TestMethodAttribute("Custom display name")]

// After (v4)
[TestMethodAttribute(DisplayName = "Custom display name")]
```
