# Java Extension

Language-specific guidance for Java test generation.

## Rule #1: Investigate the Repo First

Before writing any test or running any command, read:

1. **Existing tests** — find `*Test.java` / `*Tests.java` / `*IT.java` (integration) files and copy their style (JUnit version, assertion library, mock library, lifecycle methods)
2. **Build file** — `pom.xml` (Maven), `build.gradle` / `build.gradle.kts` (Gradle), `BUILD` / `BUILD.bazel` (Bazel)
3. **Java version** — `<maven.compiler.release>`, `sourceCompatibility`, or `toolchains` block
4. **Wrapper scripts** — always prefer `./mvnw` or `./gradlew` over a system-installed Maven/Gradle so you match the project's pinned version

Use whatever framework the repo already uses (JUnit 4, JUnit 5/Jupiter, TestNG). Do not migrate to a different framework as a side effect of writing tests.

## Build Tool Detection

| Indicator | Build tool | Default test command |
|-----------|------------|----------------------|
| `pom.xml` | Maven | `./mvnw test` |
| `build.gradle` / `build.gradle.kts` | Gradle | `./gradlew test` |
| `settings.gradle*` with `include 'subproject'` | Gradle multi-project | `./gradlew :subproject:test` |
| `BUILD` / `BUILD.bazel` | Bazel | `bazel test //path/to:test` |

If both `pom.xml` and `build.gradle` exist, pick the one used by CI.

## Build Commands

| Scope | Maven | Gradle |
|-------|-------|--------|
| Compile main + test | `./mvnw test-compile` | `./gradlew testClasses` |
| Compile only | `./mvnw compile` | `./gradlew classes` |
| Full build | `./mvnw verify` | `./gradlew build` |
| Skip tests during build | `./mvnw -DskipTests package` | `./gradlew assemble` |

- Use `-q` (Maven) / `--console=plain` (Gradle) to reduce output noise
- For Gradle, prefer `--no-daemon` only in CI; locally the daemon makes incremental builds far faster

## Test Commands

| Scope | Maven | Gradle |
|-------|-------|--------|
| All unit tests | `./mvnw test` | `./gradlew test` |
| Single class | `./mvnw test -Dtest=MyClassTest` | `./gradlew test --tests MyClassTest` |
| Single method | `./mvnw test -Dtest=MyClassTest#myMethod` | `./gradlew test --tests MyClassTest.myMethod` |
| Tag filter (JUnit 5) | `./mvnw test -Dgroups=fast` | `./gradlew test -PincludeTags=fast` (if configured) or `--tests` |
| Integration tests | `./mvnw verify -DskipUnitTests` (with failsafe-plugin) | `./gradlew integrationTest` (if registered) |

- `Surefire` runs unit tests (`*Test.java`); `Failsafe` runs integration tests (`*IT.java`) — do not put long integration tests under Surefire
- Gradle's `--tests` accepts wildcards: `--tests "*MyMethod*"`
- Use `--rerun-tasks` (Gradle) or `-DforkCount=...` (Surefire) only when troubleshooting cache issues

## Lint Command

Use the repo's existing lint task first. Otherwise check for:

- Checkstyle (`checkstyle.xml`, `<plugin>checkstyle</plugin>`) → `./mvnw checkstyle:check` or `./gradlew checkstyleMain`
- Spotless (`spotless` block / plugin) → `./mvnw spotless:apply` or `./gradlew spotlessApply`
- ErrorProne / NullAway → integrated into compilation; run a normal build
- google-java-format / palantir-java-format → use the repo's configured formatter

Never disable existing checks in the test files you generate.

## Project Layout and Imports

Maven/Gradle conventional layout:

```
src/
├── main/java/com/example/foo/Bar.java
├── main/resources/
├── test/java/com/example/foo/BarTest.java
└── test/resources/
```

| Layout | Test placement |
|--------|----------------|
| Standard | `src/test/java/<same package as production class>/<ClassName>Test.java` |
| Integration tests separated | `src/integrationTest/java/...` (Gradle) or `src/it/java/...` (Maven w/ failsafe) |
| Multi-module Maven | Tests live in the same module as the code under test |

- Test classes must mirror the production class's **package** to access package-private members
- Avoid wildcard imports unless the repo already uses them — match the explicit imports shown in the templates below
- For JUnit 5: import `org.junit.jupiter.api.Test` (and other annotations as needed) and `org.junit.jupiter.api.Assertions.assertEquals` etc. as static imports
- For JUnit 4: import `org.junit.Test`, `org.junit.Before`, etc., and `org.junit.Assert.assertEquals` etc. as static imports

## Test Framework Detection

| Indicator | Framework | Annotations | Assertion style |
|-----------|-----------|-------------|------------------|
| `junit-jupiter-*` deps | JUnit 5 | `@Test`, `@ParameterizedTest`, `@BeforeEach`, `@DisplayName` | `Assertions.assertEquals(expected, actual)` |
| `junit:junit:4.x` | JUnit 4 | `@Test`, `@Before`, `@RunWith` | `Assert.assertEquals(expected, actual)` |
| `org.testng:testng` | TestNG | `@Test(groups=...)`, `@BeforeMethod` | `Assert.assertEquals(actual, expected)` (note **reversed** order) |
| `org.assertj:assertj-core` | AssertJ (assertions only) | n/a | `assertThat(actual).isEqualTo(expected)` |
| `org.hamcrest:hamcrest` | Hamcrest matchers | n/a | `assertThat(actual, is(equalTo(expected)))` |

**Argument order matters**: JUnit/AssertJ use `(expected, actual)`; TestNG uses `(actual, expected)`. Reversing them produces confusing failure messages.

## JUnit 5 Template

```java
package com.example.foo;

import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

class CalculatorTest {

    @Test
    @DisplayName("add returns sum of two positive numbers")
    void add_positiveNumbers_returnsSum() {
        Calculator sut = new Calculator();
        assertEquals(5, sut.add(2, 3));
    }

    @ParameterizedTest
    @CsvSource({
        "2, 3, 5",
        "-1, 1, 0"
    })
    void add_validInputs_returnsSum(int a, int b, int expected) {
        assertEquals(expected, new Calculator().add(a, b));
    }

    @Test
    void divide_byZero_throws() {
        Calculator sut = new Calculator();
        assertThrows(ArithmeticException.class, () -> sut.divide(1, 0));
    }
}
```

## Common Errors

| Error | Fix |
|-------|-----|
| `package X does not exist` | Add the dependency to `pom.xml` / `build.gradle`; run `./mvnw dependency:resolve` or `./gradlew --refresh-dependencies` |
| `cannot find symbol` | Verify class name and import path; check that the test source set sees the production source set |
| `No tests found for given includes` (Gradle) | `--tests` pattern doesn't match; verify the class/method names, that test methods are annotated with `@Test`, and that the class name matches the test task's `include` pattern (default `**/*Test*.class`). For JUnit 4 only, the class must also be `public` with a public no-arg constructor — JUnit 5 allows package-private classes and methods |
| `Test class should have exactly one public zero-argument constructor` (JUnit 4) | Remove constructors with parameters; use `@Before` for setup |
| `org.junit.runners.model.InvalidTestClassError` (JUnit 4) | Class is missing `public`, has wrong constructor, or method signature is wrong |
| Mixing `org.junit.Test` (4) and `org.junit.jupiter.api.Test` (5) | Pick one framework per test class — imports must match the framework annotation |
| `java.lang.NoClassDefFoundError` at runtime | Test runtime classpath is missing a transitive dep; add it to `testRuntimeOnly` (Gradle) or `<scope>test</scope>` (Maven) |
| `UnsupportedClassVersionError` | JDK used to run tests is older than the JDK used to compile; align toolchains |
| `Mockito cannot mock final class` | Use Mockito's inline mock maker — Mockito 5+ uses it by default; for Mockito 3.x/4.x add the `mockito-inline` artifact (replaces `mockito-core`). Or switch to MockK for Kotlin. `mockito-subclass` does **not** mock final classes |
| `WrongTypeOfReturnValue` (Mockito) | The stubbed method returns a different type than the mock was set up for — check return type signatures |

## Mocking Rules

- Use whatever the repo already uses: **Mockito** (most common), **EasyMock**, **JMockit**, or hand-written fakes
- For JUnit 5 + Mockito, use `@ExtendWith(MockitoExtension.class)` with `@Mock` / `@InjectMocks` fields
- For JUnit 4 + Mockito, use `@RunWith(MockitoJUnitRunner.class)` or `MockitoAnnotations.openMocks(this)` in `@Before`
- Use `when(mock.method(...)).thenReturn(...)` for stubs and `verify(mock).method(...)` for interactions
- Use `ArgumentCaptor` to assert on complex argument values rather than over-specifying matchers
- Prefer constructor injection so production code stays testable without `@InjectMocks`
- If a test needs more than 3 mocks, flag it as a design smell

## Spring Boot

If the repo uses Spring Boot:

- `@SpringBootTest` loads the full context — slow; use only when needed
- Slice tests are faster: `@WebMvcTest`, `@DataJpaTest`, `@JsonTest`
- Use `@MockBean` (Spring) only inside Spring tests; in plain unit tests use `@Mock`
- Use `@Testcontainers` for real-DB integration tests if the repo already has it on the classpath

## Dependency Installation (Last Resort)

Only add dependencies after investigation confirms they are missing.

Maven (`pom.xml`):

```xml
<dependency>
    <groupId>org.junit.jupiter</groupId>
    <artifactId>junit-jupiter</artifactId>
    <version>5.10.2</version>
    <scope>test</scope>
</dependency>
```

Gradle (`build.gradle.kts`):

```kotlin
testImplementation("org.junit.jupiter:junit-jupiter:5.10.2")
testRuntimeOnly("org.junit.platform:junit-platform-launcher")
```

If the repo uses BOMs (`<dependencyManagement>` or Gradle platforms), reuse them — don't pin a different version than the BOM publishes.

## Skip Coverage Tools

Do not configure or run coverage tools (JaCoCo, Cobertura, OpenClover). Coverage is measured separately by the evaluation harness.
