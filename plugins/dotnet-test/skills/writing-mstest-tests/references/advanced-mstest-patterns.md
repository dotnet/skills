# Advanced MSTest Patterns

Use these patterns only after checking the installed MSTest version.

## Retry flaky tests

`Retry` requires MSTest 3.8 or later. Use it only for genuinely flaky external
dependencies such as a network or file system, not to hide race conditions or
shared-state defects. Use bounded attempts and a nonzero delay or backoff.

```csharp
[TestMethod]
[Retry(
    3,
    MillisecondsDelayBetweenRetries = 1_000,
    BackoffType = DelayBackoffType.Exponential)]
public async Task ExternalService_EventuallyResponds()
{
    var response = await WeatherClient.GetAsync();
    Assert.IsNotNull(response);
}
```

## Conditional execution

`OSCondition` requires MSTest 3.8 or later. `CICondition` requires MSTest 3.10
or later.

```csharp
[TestMethod]
[OSCondition(OperatingSystems.Windows)]
public void WindowsRegistry_ReadsValue() { }

[TestMethod]
[CICondition(ConditionMode.Exclude)]
public void LocalOnly_InteractiveTest() { }
```

Attributes replace environment branches in test bodies. They do not replace
the operation under test. Keep the real registry, GPU, or service operation,
the assertion, and concrete resource cleanup. Initialize cleanup state safely
and release it symmetrically, including a null guard when setup can fail.

## Parallelization

```csharp
[assembly: Parallelize(Workers = 4, Scope = ExecutionScope.MethodLevel)]

[TestClass]
[DoNotParallelize]
public sealed class DatabaseIntegrationTests { }
```

## MSTest analyzer diagnostics

`MSTest.Analyzers` reports `MSTESTxxxx` diagnostics during build and in the IDE.
The analyzers come with the modern `MSTest` metapackage and `MSTest.Sdk`, and
with `MSTest.TestFramework` 3.7 or later. Add an explicit analyzer package only
when the user asks to adopt analyzers.

Apply the version-compatible fix instead of suppressing the rule. For a rule
not listed here, use the official
[MSTest analyzer reference](https://learn.microsoft.com/dotnet/core/testing/mstest-analyzers/overview).

| Rule | Problem | Fix |
|---|---|---|
| MSTEST0006 | `[ExpectedException]` used | On 3.8+, use `Assert.Throws<T>` or `Assert.ThrowsExactly<T>`; otherwise use `Assert.ThrowsException<T>` |
| MSTEST0017 | `Assert.AreEqual` arguments swapped | Put `expected` first and `actual` second |
| MSTEST0023 | Negated Boolean assertion | Use `Assert.IsFalse` |
| MSTEST0025 | Always-false condition asserted | Use `Assert.Fail("reason")` |
| MSTEST0032 | Always-true condition asserted | Remove or correct the assertion |
| MSTEST0037 | Suboptimal generic assertion | Use the specific assertion from workflow Step 3 |
| MSTEST0038 | `Assert.AreSame` on value types | Use `Assert.AreEqual` |
| MSTEST0039 | Legacy `Assert.ThrowsException` | On 3.8+, use `Assert.Throws` or `Assert.ThrowsExactly` and their async variants |
| MSTEST0044 | `[DataTestMethod]` used | Use `[TestMethod]` only when the installed version supports data rows on it |
| MSTEST0046 | `StringAssert` used | On 3.10+, use the equivalent `Assert` method |
| MSTEST0052 | Explicit `DynamicDataSourceType` | Remove it because the source type is inferred |
| MSTEST0042 / MSTEST0060 | Duplicate data or test attribute | Remove the duplicate attribute |
| MSTEST0024 | Static `TestContext` field | Make it an instance member |
| MSTEST0045 / MSTEST0049 / MSTEST0054 | Timeout or token is not cooperative | Flow the supported `TestContext` cancellation token into the awaited call |
| MSTEST0036 | Member shadows a base test member | Rename it or use `override` instead of `new` |
| MSTEST0061 | Runtime OS check inside a test | Use `[OSCondition(...)]` |
| MSTEST0002 / MSTEST0003 / MSTEST0005 / MSTEST0007-0014 | Invalid test or fixture layout | Correct the signature named by the rule |

## Analyzer mode

Use `MSTestAnalysisMode` with MSTest 3.8 or later to control the rule set:

```xml
<PropertyGroup>
  <!-- None | Default | Recommended | All -->
  <MSTestAnalysisMode>Recommended</MSTestAnalysisMode>
</PropertyGroup>
```

`Recommended` escalates information rules to warnings and is suitable for most
projects. Some rules, including MSTEST0015 and MSTEST0019-0022, are opt-in and
must be enabled through `.editorconfig`.
