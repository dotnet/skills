# Kotlin Test Frameworks Reference (JUnit 5, Kotest, MockK)

Reference data for analyzing Kotlin test code. Used by the polyglot test analysis skills.

## Capability tags

| Capability | Support |
|------------|---------|
| Test discovery | Strong — JUnit 5 conventions, Kotest spec classes |
| Assertion detection | Strong — JUnit + Kotest matchers + MockK verifications |
| Sleep/delay detection | Strong — `Thread.sleep`, `delay()` |
| Skip/ignore detection | Strong — `@Disabled`, `.config(enabled = false)` |
| Setup/teardown detection | Strong — JUnit + Kotest lifecycle |
| Tag support | **auto-edit** — JUnit 5 `@Tag`, Kotest `tags`, project-defined |

## Test File Identification

| Framework | File convention | Test method markers |
|-----------|----------------|---------------------|
| JUnit 5 (Jupiter) | `*Test.kt`, `*Tests.kt`, `*IT.kt` | `@Test fun foo()` |
| Kotest | `*Spec.kt` (any style) | inherits a spec class (`StringSpec`, `FunSpec`, `BehaviorSpec`, `ShouldSpec`, `DescribeSpec`, `FeatureSpec`, `WordSpec`, `FreeSpec`, `AnnotationSpec`) |
| Spek | `*Spec.kt` | `object FooSpec : Spek({ ... })` |
| TestNG | `*Test.kt` | `@Test fun foo()` (TestNG annotation) |

## Assertion APIs

| Category | JUnit 5 (`Assertions`) | Kotest matchers | AssertK |
|----------|------------------------|-----------------|---------|
| Equality | `assertEquals(expected, actual)` | `actual shouldBe expected` | `assertThat(actual).isEqualTo(expected)` |
| Boolean | `assertTrue(b)` / `assertFalse(b)` | `b.shouldBeTrue()` / `b.shouldBeFalse()` | `assertThat(b).isTrue()` |
| Null | `assertNull(x)` / `assertNotNull(x)` | `x.shouldBeNull()` / `x.shouldNotBeNull()` | `assertThat(x).isNull()` |
| Throws | `assertThrows<SomeException> { … }` | `shouldThrow<SomeException> { … }` | `assertFailure { … }.isInstanceOf(SomeException::class)` |
| Type | `assertTrue(x is T)` | `x.shouldBeInstanceOf<T>()` | `assertThat(x).isInstanceOf(T::class)` |
| String | `assertTrue(s.contains(sub))` | `s shouldContain sub` / `s shouldMatch Regex("...")` | `assertThat(s).contains(sub)` |
| Collection | `assertIterableEquals(...)` | `col shouldContainExactly listOf(...)` | `assertThat(col).containsExactly(...)` |
| Coroutine result | `runTest { ... }` block + assertEquals | `coroutineScope { ... } shouldBe expected` | within `runTest` |
| Fail | `fail("reason")` | `fail("reason")` (Kotest) | `Assertions.fail("reason")` |

MockK verifications: `verify(exactly = 1) { mock.method() }` — counts as a state/side-effect assertion.

## Sleep/Delay Patterns

| Pattern | Example |
|---------|---------|
| Thread sleep | `Thread.sleep(2000)` |
| Coroutine delay | `delay(1000)` inside `runBlocking { ... }` |
| Acceptable (coroutine test) | `runTest { advanceTimeBy(1000) }` (virtual time, no real wait) |
| Awaitility-style | `Awaitility.await().atMost(5, SECONDS).until { ... }` |

Real `delay` inside `runBlocking { }` is a sleep smell; inside `runTest { }` it's virtual time and acceptable.

## Skip/Ignore Annotations

| Framework | Annotation |
|-----------|------------|
| JUnit 5 | `@Disabled`, `@Disabled("reason")`, `@DisabledIf(...)`, `@EnabledIf(...)`, `@DisabledOnOs(OS.WINDOWS)` |
| JUnit 5 (dynamic) | `Assumptions.assumeTrue(cond)` |
| Kotest | `.config(enabled = false)`, `xtest("…")`, `xshould("…")`, `xdescribe("…")` |
| Kotest (project-wide) | `EnabledCondition` / `EnabledIf` extensions |
| TestNG | `@Test(enabled = false)`, `throw SkipException("reason")` |

## Exception Handling — Idiomatic Alternatives

```kotlin
// JUnit 5:
val ex = assertThrows<InvalidOrderException> {
    service.placeOrder(emptyOrder)
}
assertEquals("at least one item", ex.message)

// Kotest:
val ex = shouldThrow<InvalidOrderException> {
    service.placeOrder(emptyOrder)
}
ex.message shouldContain "at least one item"

// AssertK:
assertFailure { service.placeOrder(emptyOrder) }
    .isInstanceOf(InvalidOrderException::class)
    .messageContains("at least one item")
```

Flag manual `try { ... fail() } catch (e: SomeException) { ... }` patterns.

## Mystery Guest — Common Kotlin/Android Patterns

| Indicator | What to look for |
|-----------|------------------|
| File system | `File(path).readText()`, hard-coded paths |
| Database | `Room.databaseBuilder(...)` without `inMemoryDatabaseBuilder`, real `Exposed` against file/server |
| Network | `Retrofit.create<…>()` against a real base URL, `OkHttp` without `MockWebServer` |
| Environment | `System.getenv("X")` |
| Android | `Context.assets.open(...)`, file system writes to internal/external storage |
| Acceptable | `MockWebServer`, `MockK`, `inMemoryDatabaseBuilder`, `@MockK`, Robolectric (acknowledged-integration), `TemporaryFolder` |

## Integration Test Markers

- File suffix: `*IT.kt`, `*IntegrationTest.kt`, `*E2ETest.kt`
- Annotations: `@SpringBootTest`, `@DataJpaTest`, `@Tag("integration")`
- Kotest tags: `tag = listOf(IntegrationTag)`
- Android: `androidTest/` source set is on-device/instrumented (integration); `test/` is JVM (unit)
- Use of Testcontainers, embedded servers

## Setup/Teardown

| Framework | Per-test | Per-class |
|-----------|----------|-----------|
| JUnit 5 | `@BeforeEach` | `@BeforeAll` (must be `@JvmStatic` in companion object unless `@TestInstance(PER_CLASS)`) |
| JUnit 5 | `@AfterEach` | `@AfterAll` |
| Kotest | `beforeTest { }` / `beforeEach { }` | `beforeSpec { }` |
| Kotest | `afterTest { }` / `afterEach { }` | `afterSpec { }` |
| TestNG | `@BeforeMethod` | `@BeforeClass`, `@BeforeSuite` |
| Spek | `beforeEachTest { }` | `beforeGroup { }` |

## Tag/Trait Attributes (for `test-tagging`)

| Framework | Tag mechanism | Example |
|-----------|---------------|---------|
| JUnit 5 | `@Tag("positive")` (stackable) | `@Tag("positive") @Tag("critical-path")` |
| Kotest | per-test: `.config(tags = setOf(Positive))`; per-spec: `override fun tags() = setOf(Positive)` | tag objects: `object Positive : Tag()` |
| TestNG | `@Test(groups = ["positive"])` | `@Test(groups = ["positive", "boundary"])` |

For JUnit 5 in Gradle, register tag filters in `build.gradle.kts`:

```kotlin
tasks.test {
    useJUnitPlatform {
        includeTags("positive")
        excludeTags("slow")
    }
}
```

## Language-specific calibration notes

- **Coroutine tests must use `runTest` / `runBlocking`** at the boundary; missing wrapper makes the test silently incomplete. Flag `suspend fun` test bodies without a coroutine scope.
- **`runBlocking` vs `runTest`:** `runBlocking` waits in real time; `runTest` uses virtual time. Prefer `runTest` for testing time-dependent code.
- **MockK `verify { }`** without `exactly = N` only checks at least once. Tests asserting exact behavior should set the count.
- **Kotest's `forAll(...)` (data-driven)** is parametrized, NOT duplicate tests.
- **`@OptIn(ExperimentalCoroutinesApi::class)`** is common in coroutine tests — not a smell.
- **Android `@MediumTest` / `@LargeTest`** are size annotations from `androidx.test.filters`; treat as integration markers.
- **Compose UI tests** (`createComposeRule`) are UI integration tests.
- **Bare `assert(x)` in tests** is the Kotlin `kotlin.assert` — acceptable but recommend framework matchers for richer failure messages.
- **`shouldBe` chained Kotest matchers** are single conceptual assertions; do not over-count chain length.
