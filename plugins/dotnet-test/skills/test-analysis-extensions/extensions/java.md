# Java Test Frameworks Reference (JUnit 4, JUnit 5 / Jupiter, TestNG)

Reference data for analyzing Java test code. Used by the polyglot test analysis skills.

## Capability tags

| Capability | Support |
|------------|---------|
| Test discovery | Strong — annotations + Maven Surefire / Gradle conventions |
| Assertion detection | Strong — `Assertions.*`, `assertThat` (AssertJ/Hamcrest) |
| Sleep/delay detection | Strong — `Thread.sleep`, `Awaitility`, `TimeUnit.sleep` |
| Skip/ignore detection | Strong — `@Disabled`, `@Ignore`, `Assume.*` |
| Setup/teardown detection | Strong — `@BeforeEach`, `@BeforeAll`, etc. |
| Tag support | **auto-edit** — JUnit 5 `@Tag`, JUnit 4 `@Category`, TestNG `groups` |

## Test File Identification

| Framework | File convention | Test method markers |
|-----------|----------------|---------------------|
| JUnit 4 | `*Test.java`, `*Tests.java`, `*IT.java` (integration) | `@Test`, classes typically `public` |
| JUnit 5 (Jupiter) | same conventions | `@Test`, `@ParameterizedTest`, `@RepeatedTest`, `@TestFactory`, `@TestTemplate` |
| TestNG | `*Test.java` | `@Test` (org.testng.annotations.Test) |

## Assertion APIs

| Category | JUnit 4 (`Assert`) | JUnit 5 (`Assertions`) | TestNG (`Assert`) | AssertJ (`assertThat`) |
|----------|--------------------|------------------------|-------------------|------------------------|
| Equality | `assertEquals(expected, actual)` | `assertEquals(expected, actual)` | `assertEquals(actual, expected)` (note arg order!) | `assertThat(actual).isEqualTo(expected)` |
| Boolean | `assertTrue(b)` / `assertFalse(b)` | `assertTrue(b)` / `assertFalse(b)` | `assertTrue(b)` | `assertThat(b).isTrue()` |
| Null | `assertNull(x)` / `assertNotNull(x)` | `assertNull(x)` | `assertNull(x)` | `assertThat(x).isNull()` |
| Exception | `@Test(expected = X.class)` / `try…catch` | `assertThrows(X.class, () -> {…})` | `assertThrows(X.class, () -> {…})` / `expectedExceptions = X.class` | `assertThatThrownBy(() -> {…}).isInstanceOf(X.class)` |
| Type | `assertTrue(x instanceof T)` | `assertInstanceOf(T.class, x)` | `assertTrue(x instanceof T)` | `assertThat(x).isInstanceOf(T.class)` |
| String | `assertEquals` then `contains` | `assertTrue(s.contains(sub))` | `assertEquals(s, expected)` | `assertThat(s).contains(sub).startsWith(...)` |
| Collection | `assertEquals(list, expected)` | `assertIterableEquals(...)` | `assertEqualsNoOrder(actual, expected)` | `assertThat(col).containsExactly(...).hasSize(n)` |
| Fail | `fail("reason")` | `fail("reason")` | `fail("reason")` | `Assertions.fail("reason")` |

**TestNG quirk:** `Assert.assertEquals(actual, expected)` reverses the argument order vs JUnit. Misordered arguments are a common smell.

Third-party libraries: AssertJ (`assertThat`), Hamcrest (`assertThat(x, is(y))`), Truth (Google), Mockito (`verify(mock).method(...)`).

## Sleep/Delay Patterns

| Pattern | Example |
|---------|---------|
| Thread sleep | `Thread.sleep(2000)` |
| TimeUnit sleep | `TimeUnit.SECONDS.sleep(2)` |
| Awaitility (acceptable) | `await().atMost(5, SECONDS).until(() -> condition)` — replaces sleep with polling |
| CompletableFuture timeouts | `future.get(5, TimeUnit.SECONDS)` |

Flag raw `Thread.sleep` in tests as Sleepy Test. Awaitility-based waits are acceptable.

## Skip/Ignore Annotations

| Framework | Annotation |
|-----------|------------|
| JUnit 4 | `@Ignore`, `@Ignore("reason")` |
| JUnit 5 | `@Disabled`, `@Disabled("reason")`, `@DisabledOnOs`, `@EnabledIfSystemProperty`, `@EnabledIf(...)` |
| JUnit 4/5 (dynamic) | `Assume.assumeTrue(cond)`, `Assumptions.assumeTrue(cond)` |
| TestNG | `enabled = false` on `@Test`, `@Test(enabled = false)`, `throw new SkipException("reason")` |

## Exception Handling — Idiomatic Alternatives

```java
// JUnit 5 (preferred):
InvalidOrderException ex = assertThrows(
    InvalidOrderException.class,
    () -> service.placeOrder(emptyOrder));
assertEquals("Order must contain at least one item", ex.getMessage());

// AssertJ:
assertThatThrownBy(() -> service.placeOrder(emptyOrder))
    .isInstanceOf(InvalidOrderException.class)
    .hasMessageContaining("at least one item");

// TestNG:
@Test(expectedExceptions = InvalidOrderException.class,
      expectedExceptionsMessageRegExp = ".*at least one item.*")
public void placeOrder_empty_throws() { service.placeOrder(emptyOrder); }
```

Flag legacy JUnit 4 `@Test(expected=...)` and bare `try/catch/fail` patterns as smells.

## Mystery Guest — Common Java Patterns

| Indicator | What to look for |
|-----------|------------------|
| File system | `Files.readString`, `new File(...)`, hard-coded paths |
| Database | `DriverManager.getConnection`, real Spring `@SpringBootTest(webEnvironment = RANDOM_PORT)` without `@MockBean`, `JdbcTemplate` against real DB |
| Network | `HttpClient.send`, `RestTemplate.getForObject`, raw `Socket` |
| Environment | `System.getenv`, `System.getProperty` (without test default) |
| Acceptable | `@TempDir`, `MockWebServer` (OkHttp), `WireMock`, Testcontainers (acknowledged-integration), H2 in-memory, `@MockBean`, `MockMvc` |

## Integration Test Markers

- File suffix: `*IT.java` (Failsafe convention), `*IntegrationTest.java`, `*E2ETest.java`
- Annotations: `@SpringBootTest`, `@DataJpaTest`, `@Tag("integration")`, `@Category(IntegrationTests.class)` (JUnit 4)
- TestNG: `@Test(groups = {"integration"})`
- Use of Testcontainers, embedded Kafka/Mongo, or `@Sql` scripts

## Setup/Teardown

| Framework | Per-test | Per-class |
|-----------|----------|-----------|
| JUnit 4 | `@Before` | `@BeforeClass` (static) |
| JUnit 4 | `@After` | `@AfterClass` (static) |
| JUnit 5 | `@BeforeEach` | `@BeforeAll` (static unless `@TestInstance(Lifecycle.PER_CLASS)`) |
| JUnit 5 | `@AfterEach` | `@AfterAll` |
| TestNG | `@BeforeMethod` | `@BeforeClass`, `@BeforeSuite`, `@BeforeGroups` |
| TestNG | `@AfterMethod` | `@AfterClass`, `@AfterSuite`, `@AfterGroups` |

## Tag/Trait Attributes (for `test-tagging`)

| Framework | Tag mechanism | Example |
|-----------|---------------|---------|
| JUnit 5 | `@Tag("name")` (stackable) | `@Tag("positive")`, `@Tag("boundary")` |
| JUnit 4 | `@Category(NegativeTests.class)` (requires marker interfaces) | `@Category({NegativeTests.class, BoundaryTests.class})` |
| TestNG | `@Test(groups = {"name"})` | `@Test(groups = {"positive", "critical-path"})` |

For JUnit 4, marker interfaces must exist (e.g., `interface NegativeTests {}`). Suggest creating them rather than dropping `@Category` references with no target.

For Maven Surefire, register groups in `pom.xml`:

```xml
<configuration>
    <groups>positive,critical-path</groups>
</configuration>
```

## Language-specific calibration notes

- **Argument-order trap (TestNG):** `Assert.assertEquals(actual, expected)` reverses JUnit's order. Misordered comparisons produce backwards failure messages but still pass/fail correctly. Flag as smell when reviewing TestNG suites.
- **JUnit 4 `@Test(expected=...)`** loses precise exception location and accepts subclasses; recommend migrating to `assertThrows`.
- **`@SpringBootTest`** bootstraps the entire application — almost always an integration test.
- **AssertJ chaining** is a single assertion conceptually; do not count each chained `.has...` as a separate assertion for assertion-count metrics.
- **Mockito `verify(...)`** counts as a state/side-effect assertion when used to assert behavior — do not flag tests that only `verify` as assertion-free.
- **Lombok `@SneakyThrows`** in tests is acceptable; do not flag.
- **Parameterized tests** (`@ParameterizedTest` + `@MethodSource` / `@ValueSource`) are NOT duplicate tests; they are the consolidated form.
