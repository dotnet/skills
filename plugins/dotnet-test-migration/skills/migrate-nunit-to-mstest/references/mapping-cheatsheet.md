# NUnit -> MSTest Mapping Cheatsheet

Load only the sections needed for constructs found in the project. Target MSTest is v4; features introduced in a specific v4 release are marked.

## Official attribute coverage index

Every attribute on NUnit's official attribute page is classified below or in a named section of this reference.

| Disposition | NUnit attributes |
|---|---|
| Direct or close mapping | `Apartment`, `Author`, `CancelAfter`, `Category`, `Combinatorial`, `Description`, `FixtureLifeCycle`, `Ignore`, `LevelOfParallelism`, `MaxTime`, `NonParallelizable`, `OneTimeSetUp`, `OneTimeTearDown`, `Parallelizable`, `Property`, `Random`, `Range`, `Retry`, `SetUp`, `SetUpFixture`, `TearDown`, `Test`, `TestCase`, `TestCaseSource`, `TestFixture`, `Timeout`, `Values`, `ValueSource` |
| Explicit semantic rewrite | `Culture`, `Datapoint`, `DatapointSource`, `DefaultFloatingPointTolerance`, `Order`, `Pairwise`, `Platform`, `Repeat`, `Sequential`, `SetCulture`, `SetUICulture`, `TestFixtureSource`, `TestOf`, `Theory` |
| Remove or normalize | `NonTestAssembly`, `TestFixtureSetUp`, `TestFixtureTearDown` |
| No behavior-identical MSTest attribute | `Explicit`, `RequiresThread`, `SingleThreaded` |

## 1. Discovery and metadata

| NUnit | MSTest |
|---|---|
| `[TestFixture]` | `[TestClass]` |
| `[Test]` | `[TestMethod]` |
| `[Test(Description = "x")]` | `[TestMethod(DisplayName = "x")]` when the description is used as the display name |
| `[Category("Unit")]` | `[TestCategory("Unit")]` |
| `[Property("Key", "Value")]` | `[TestProperty("Key", "Value")]` |
| `[Author("name")]` | `[Owner("name")]` only when author is used as ownership metadata; otherwise `[TestProperty("Author", "name")]` |
| `[Description("text")]` | `[Description("text")]` |
| `[Ignore("reason")]` | `[TestMethod]` + `[Ignore("reason")]` |
| `[Explicit("reason")]` | No exact equivalent. MSTest `Ignore` never runs, while NUnit explicit tests run when selected. Preserve through an agreed filtering convention or report manual follow-up. |

Parameterized `[TestFixture(...)]` and `[TestFixtureSource]` have no class-level data equivalent. Split them into concrete `[TestClass]` types, move parameters into data-driven methods, or use a custom generator. Do not discard fixture arguments.

`[TestOf(typeof(T))]` is NUnit property metadata. Map it at class or method scope to `[TestProperty("TestOf", "Namespace.T")]`, preserving the full type name as a string. MSTest `TestProperty` has no assembly target, so push assembly-level metadata to each test class or document its removal.

`[NonTestAssembly]` has no role after NUnit is removed. If the project intentionally contains no tests, remove NUnit and its adapter but do not add MSTest or turn the utility assembly into a test project.

Deprecated `[TestFixtureSetUp]` and `[TestFixtureTearDown]` first normalize to `[OneTimeSetUp]` and `[OneTimeTearDown]`, then map to static `[ClassInitialize]` and `[ClassCleanup]`.

## 2. Data-driven tests

| NUnit | MSTest |
|---|---|
| `[TestCase(1, 2)]` | `[TestMethod]` + `[DataRow(1, 2)]` |
| `[TestCaseSource(nameof(Cases))]` | `[TestMethod]` + `[DynamicData(nameof(Cases))]` |
| `TestCaseData` with name/category/ignore | `TestDataRow<T>` carrying `DisplayName`, `TestCategories`, and `IgnoreMessage` |
| `TestCaseData` custom properties | No direct `TestDataRow<T>` equivalent; use a custom data source or move stable metadata to the test method/class |
| `[Combinatorial]` | `[CombinatorialData]` (MSTest 4.4+) |
| parameter `[Values(...)]` | `[CombinatorialValues(...)]` (MSTest 4.4+) |
| parameter `[Range(from, to)]` | `[CombinatorialRange(from, to, 1)]` (MSTest 4.4+); the two-argument MSTest constructor means `from, count`, not `from, to` |
| parameter `[Range(from, to, step)]` | `[CombinatorialRange(from, to, step)]` (MSTest 4.4+) |
| parameter `[Random(min, max, count)]` | `[CombinatorialRandomData(Minimum = min, Maximum = max, Count = count)]` (MSTest 4.4+) |
| `[Sequential]` | Explicit `[DataRow]` pairs or one `[DynamicData]` source; Cartesian combinatorial data is not equivalent |
| `[ValueSource]` | `[DynamicData]` or a custom `ITestDataSource` |
| `[Pairwise]` | Preserve the exact generated rows in `[DynamicData]`, or use a verified pairwise `ITestDataSource`; `[CombinatorialData]` is not equivalent |

`[TestCase(ExpectedResult = value)]` and `new TestCaseData(...).Returns(value)` require a source rewrite:

```csharp
// NUnit
[TestCase(2, 3, ExpectedResult = 5)]
public int Add(int left, int right) => left + right;

// MSTest
[TestMethod]
[DataRow(2, 3, 5)]
public void Add(int left, int right, int expected)
    => Assert.AreEqual(expected, left + right);
```

MSTest `[DataRow]` values must exactly match parameter types. Audit numeric suffixes, enums, arrays, and nullable values.

MSTest combinatorial attributes live in `Microsoft.VisualStudio.TestTools.UnitTesting.Combinatorial`; add that using when any of them are emitted.

### NUnit theories

NUnit `[Theory]` discovers values by type from `[Datapoint]` and `[DatapointSource]`, combines values across parameters, and supplies all bool or enum values automatically when no explicit datapoints exist. MSTest has no theory attribute with those discovery rules.

1. Gather matching datapoints from the fixture type, including inherited fixture members. When `searchInDeclaringTypes` is enabled, additionally traverse the fixture's lexically enclosing `DeclaringType` chain.
2. Materialize the same rows through `[DynamicData]` or MSTest combinatorial attributes.
3. Explicitly enumerate `false/true` and all enum values that NUnit supplied automatically.
4. Convert `Assume.That(...)` to row-level `Assert.Inconclusive(...)` or an equivalent condition.
5. Capture the source runner's observable all-assumptions-invalid result before editing. NUnit defines an aggregate theory failure, but the NUnit VSTest adapter can expose only skipped child cases and return success. Preserve the actual runner-visible outcome unless the user explicitly wants the framework-level aggregate rule.

## 3. Assertions

### Equality and collections

| NUnit constraint/classic form | MSTest |
|---|---|
| `Assert.That(actual, Is.EqualTo(expected))` for scalars | `Assert.AreEqual(expected, actual)` |
| `Assert.That(actual, Is.Not.EqualTo(expected))` | `Assert.AreNotEqual(expected, actual)` |
| `Assert.That(actual, Is.SameAs(expected))` | `Assert.AreSame(expected, actual)` |
| `Assert.That(actual, Is.Null)` | `Assert.IsNull(actual)` |
| `Assert.That(actual, Is.Not.Null)` | `Assert.IsNotNull(actual)` |
| `Assert.That(actual, Is.Empty)` | `Assert.IsEmpty(actual)` |
| `Assert.That(actual, Does.Contain(item))` | `Assert.Contains(item, actual)` |
| `Assert.That(actual, Is.EquivalentTo(expected))` | `CollectionAssert.AreEquivalent(expectedList, actualList)` |
| `Assert.That(actual, Is.EqualTo(expected))` for sequences | `Assert.AreSequenceEqual(expected, actual)` (MSTest 4.3+) or `CollectionAssert.AreEqual` |

NUnit `EqualConstraint` recursively compares enumerables. Determine whether operands are sequences before choosing `Assert.AreEqual`.

### Types and exceptions

| NUnit | MSTest |
|---|---|
| `Assert.That(value, Is.TypeOf<T>())` | `Assert.IsExactInstanceOfType<T>(value)` |
| `Assert.That(value, Is.InstanceOf<T>())` | `Assert.IsInstanceOfType<T>(value)` |
| `Assert.Throws<T>(...)` | `Assert.ThrowsExactly<T>(...)` |
| `Assert.That(code, Throws.TypeOf<T>())` | `Assert.ThrowsExactly<T>(...)` |
| `Assert.Catch<T>(...)` | `Assert.Throws<T>(...)` |
| `Assert.That(code, Throws.InstanceOf<T>())` | `Assert.Throws<T>(...)` |
| `Assert.DoesNotThrow(...)` | Execute directly; optionally catch and `Assert.Fail` when a clearer failure message is needed |

NUnit `Assert.Throws<T>` requires the exact exception type; `Assert.Catch<T>` accepts derived types. Do not map both to the same MSTest method.

### Constraint rewrites

| NUnit | MSTest |
|---|---|
| `Is.True` / `Is.False` | `Assert.IsTrue` / `Assert.IsFalse` |
| `Is.GreaterThan(x)` / `Is.LessThan(x)` | `Assert.IsGreaterThan(x, actual)` / `Assert.IsLessThan(x, actual)` |
| `Is.InRange(a, b)` | `Assert.IsInRange(actual, a, b)` |
| `Does.Contain`, `Does.StartWith`, `Does.EndWith` | corresponding MSTest `Assert` string/collection method |
| `Does.Match(regex)` | `Assert.MatchesRegex(regex, actual)` |
| `Has.Count.EqualTo(n)` | `Assert.HasCount(n, collection)` |
| property, `Some`, `All`, `Exactly`, `And`, `Or`, or custom constraints | Manual rewrite into explicit assertions without dropping any predicate |

`Assert.Multiple` has no behavior-identical built-in aggregation mapping. Sequential asserts change failure aggregation; use an agreed assertion library/helper or report the behavior change.

### Floating-point tolerance

`[DefaultFloatingPointTolerance(delta)]` has no MSTest attribute equivalent. Rewrite every affected `float` or `double` equality to an overload with an explicit delta. Respect NUnit's precedence: assertion-level `.Within(...)` overrides method, method overrides fixture, and fixture overrides assembly. Do not apply the delta to integer, decimal, or non-equality assertions.

## 4. Lifecycle and fixture instances

| NUnit | MSTest |
|---|---|
| `[SetUp]` | `[TestInitialize]` |
| `[TearDown]` | `[TestCleanup]` |
| `[OneTimeSetUp]` | static `[ClassInitialize]` accepting `TestContext` |
| `[OneTimeTearDown]` | static `[ClassCleanup]` |
| `[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]` | Remove attribute; MSTest already constructs a class instance per test |
| default `LifeCycle.SingleInstance` | Audit and explicitly preserve any class-shared state through static fields/lifecycle |
| `[SetUpFixture]` for the entire assembly | `[AssemblyInitialize]` / `[AssemblyCleanup]` |
| namespace-scoped `[SetUpFixture]` | Per-class lifecycle or shared helper; assembly lifecycle widens scope |

NUnit default fixture construction is the opposite of MSTest:

- NUnit default: one fixture instance shared across test cases.
- MSTest: a new test-class instance for each test.

Constructor-created mutable fields, lazy caches, counters, and objects disposed by one-time teardown require deliberate conversion. Do not assume setup attributes alone preserve them.

NUnit runs base setup before derived setup and derived teardown before base teardown. Preserve inheritance unless tests prove a safe flattening.

## 5. TestContext

| NUnit | MSTest |
|---|---|
| `TestContext.WriteLine(...)` | injected/property `TestContext.WriteLine(...)` |
| `TestContext.Out.WriteLine(...)` | `TestContext.WriteLine(...)` |
| `TestContext.Progress.WriteLine(...)` | `TestContext.WriteLine(...)`; output stream behavior may differ |
| `TestContext.CurrentContext.Test.Name` | `TestContext.TestName` |
| `TestContext.CurrentContext.Test.FullName` | combine `FullyQualifiedTestClassName` and `TestName` when needed |
| `TestContext.CurrentContext.WorkDirectory` | `TestContext.TestRunDirectory` after confirming intended directory semantics |
| `TestContext.AddTestAttachment(path)` | `TestContext.AddResultFile(path)` |
| `TestContext.CurrentContext.CancellationToken` | `TestContext.CancellationToken` |

Do not store MSTest `TestContext` in a static field.

## 6. Execution control

| NUnit | MSTest |
|---|---|
| `[Timeout(ms)]` | `[Timeout(ms)]` |
| `[CancelAfter(ms)]` on a VSTest method | `[Timeout(ms, CooperativeCancellation = true)]`; remove NUnit's injected token parameter and use `TestContext.CancellationToken` |
| fixture `[CancelAfter(ms)]` | Apply cooperative timeout to each affected test and relevant lifecycle method; verify setup/cleanup token flow |
| `[Retry(n)]` | `[Retry(n - 1)]` when `n > 1`; remove when `n = 1` |
| `[Repeat(n)]` | No direct equivalent; a loop is not identical because it produces one result and stops differently |
| `[MaxTime(ms)]` | Run code with timing and assert the elapsed threshold; not equivalent to timeout |
| `Assert.Pass()` | normally return from the test; review code after the call because NUnit stops execution immediately |
| `Assert.Ignore(reason)` | `Assert.Inconclusive(reason)` is the nearest runtime skip but reporting differs |
| `Assert.Inconclusive(reason)` | `Assert.Inconclusive(reason)` |

NUnit's retry count includes the first execution; MSTest's count is the number of retries after the first execution. NUnit `[Retry(3)]` therefore maps to MSTest `[Retry(2)]`. NUnit `RetryExceptions` filtering has no direct built-in mapping; use a custom retry attribute or report manual follow-up.

MSTest runner integration differs for cooperative token parameters. The VSTest adapter treats a `CancellationToken` method parameter as ordinary data and fails discovery/execution unless a data source supplies it. For preserved VSTest projects, use `TestContext.CancellationToken`. Retain a method token only after a probe proves the preserved MTP configuration injects it. NUnit uses `TestContext.CurrentContext.CancellationToken` in setup and teardown; map those uses to the current MSTest `TestContext.CancellationToken`.

## 7. Threading, platform, and culture

### Apartment and dedicated-thread attributes

| NUnit | MSTest |
|---|---|
| method `[Apartment(ApartmentState.STA)]` | replace `[TestMethod]` with `[STATestMethod]`; set `UseSTASynchronizationContext = true` when async continuations require the STA thread |
| fixture `[Apartment(ApartmentState.STA)]` | `[STATestClass]` |
| assembly `[Apartment(ApartmentState.STA)]` | apply `[STATestClass]` to every affected class; MSTest has no assembly-level STA attribute |
| `[Apartment(ApartmentState.MTA)]` | MSTest default, unless it overrides an inherited/source STA policy |
| `[RequiresThread]` | No direct mapping: it guarantees a fresh thread even when apartment state already matches |
| `[SingleThreaded]` | No direct mapping: `[DoNotParallelize]` serializes but does not guarantee one thread across class initialization, tests, and cleanup |

For `RequiresThread` or `SingleThreaded`, first determine whether tests observe thread identity, thread-local state, COM ownership, or synchronization context. Use a custom `TestMethodAttribute`/executor or a deliberately managed thread when identity matters. `STATestClass` is sufficient only when the real requirement is STA, not merely "same thread."

### Platform and culture gates

| NUnit | MSTest |
|---|---|
| `[Platform]` OS values | `[OSCondition]` with `ConditionMode.Include` or `Exclude` |
| `[Platform]` process architecture values | `[ArchitectureCondition]` |
| legacy OS versions, runtime family/version, or mixed platform expressions | custom `[MemberCondition]` helper that returns the same Boolean decision |
| `[Culture]` include/exclude | custom `[MemberCondition]` helper over `CultureInfo.CurrentCulture`, preserving neutral-culture matching |

Do not replace conditional attributes with `[Ignore]`; NUnit evaluates them per environment, while `Ignore` is unconditional. Preserve the skip reason where the target condition API supports one.

### Culture mutation

`[SetCulture]` and `[SetUICulture]` change state for the duration of a method, fixture, or assembly scope and restore it afterward. MSTest has no direct attributes.

- Save the original `CultureInfo.CurrentCulture` and/or `CurrentUICulture`.
- Set the requested value in constructor/`TestInitialize`, class initialization, or assembly initialization matching the source scope.
- Restore it in the corresponding cleanup, including failure paths.
- Do not combine unrelated culture scopes into one global setting.
- Review parallel execution: culture mutation must not leak to sibling tests or tasks.

## 8. Parallelization and ordering

| NUnit | MSTest |
|---|---|
| no parallel attributes | no `[assembly: Parallelize]` |
| `[assembly: Parallelizable(ParallelScope.Fixtures)]` | `[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.ClassLevel)]` |
| `[assembly: LevelOfParallelism(N)]` | `Workers = N` on `[assembly: Parallelize]` |
| `[NonParallelizable]` | `[DoNotParallelize]` when assembly parallelization is enabled |
| fixture `[Parallelizable(ParallelScope.Children)]` | method-level parallelism only after fixture-state safety review |
| shared named resource | MSTest 4.4 `[ResourceLock("name")]` can preserve a narrower serialization boundary |
| `[Order(n)]` | No direct general ordering equivalent |

MSTest 4.4 `[DependsOn]` expresses prerequisites, not arbitrary ordering. Use it only when a test truly depends on another test's successful completion.

## 9. Packages

Remove NUnit packages and adapters that are no longer used:

- `NUnit`
- `NUnit3TestAdapter`
- `NUnit.Analyzers`
- `NUnit.ConsoleRunner`
- `NUnit.Console`
- NUnit-specific MTP adapter packages

Keep framework-agnostic assertion, mocking, snapshot, and fixture libraries. Replace NUnit integrations such as `Verify.NUnit` with their MSTest counterpart when available.

Resolve the current stable MSTest v4 version from configured sources. For VSTest, preserve a compatible explicit `Microsoft.NET.Test.Sdk` pin when the source project owns one. For MTP, prefer `MSTest.Sdk` or configure the metapackage with `EnableMSTestRunner=true` and executable output.
