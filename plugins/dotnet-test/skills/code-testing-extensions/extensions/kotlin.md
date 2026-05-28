# Kotlin Extension

Language-specific guidance for Kotlin test generation. For pure-Java codebases, use `java.md` instead.

## Rule #1: Investigate the Repo First

Before writing any test or running any command, read:

1. **Existing tests** — find files in `src/test/kotlin/`, `src/commonTest/kotlin/`, `src/jvmTest/kotlin/`, etc., and copy their style (framework, assertion library, mock library, coroutine helpers)
2. **Build file** — `build.gradle.kts` / `build.gradle` — note Kotlin version, plugins (`kotlin("jvm")`, `kotlin("multiplatform")`, `kotlin("android")`), and `dependencies { testImplementation(...) }`
3. **`gradle/libs.versions.toml`** — the version catalog if the repo uses one; reference aliases instead of hard-coded versions
4. **Wrapper script** — always invoke `./gradlew` (Unix) or `.\gradlew.bat` (Windows), never a system-installed Gradle
5. **Multiplatform layout** — `src/<sourceSet>/kotlin/` indicates KMP; tests live in matching `*Test` source sets (`commonTest`, `jvmTest`, `nativeTest`)

Use whatever framework the repo already uses (JUnit Jupiter, JUnit 4, Kotest, kotlin.test). Do not switch.

## Project Type Detection

| Indicator | Project type |
|-----------|--------------|
| `kotlin("jvm")` plugin | Plain JVM Kotlin |
| `kotlin("multiplatform")` plugin with `kotlin { jvm(); js(); ... }` | Kotlin Multiplatform |
| `com.android.application` / `com.android.library` plugin | Android |
| `org.springframework.boot` plugin | Spring Boot Kotlin |
| `kotlin("jvm")` + `application` plugin | Kotlin CLI / server |

For **Android**, see also platform-specific test types: `src/test/` for unit tests on the JVM, `src/androidTest/` for instrumented tests on a device/emulator. They use different runners and gradle tasks.

## Build Commands

| Scope | Command |
|-------|---------|
| Compile main + test (JVM) | `./gradlew compileTestKotlin` |
| Full build | `./gradlew build` |
| Skip tests | `./gradlew assemble` |
| Single module | `./gradlew :module-name:build` |
| KMP target only | `./gradlew :module:jvmTest` (or `linuxX64Test`, etc.) |

- Use `--console=plain` to suppress Gradle's animated output
- Use `--build-cache` (often default in CI) to reuse outputs
- For Android: `./gradlew assembleDebug` (build APK) and `./gradlew testDebugUnitTest` (run unit tests)

## Test Commands

| Scope | Command |
|-------|---------|
| All tests (JVM) | `./gradlew test` |
| Single class | `./gradlew test --tests "com.example.WidgetTest"` |
| Single method | `./gradlew test --tests "com.example.WidgetTest.add returns sum"` |
| KMP all targets | `./gradlew allTests` |
| KMP one target | `./gradlew jvmTest`, `./gradlew jsTest`, `./gradlew linuxX64Test` |
| Android unit tests | `./gradlew testDebugUnitTest` |
| Android instrumented | `./gradlew connectedDebugAndroidTest` (requires device/emulator) |

- `--tests` accepts wildcards: `--tests "*Widget*"`. Method names with spaces or backticks must be quoted: `--tests "com.example.WidgetTest.creates a widget"`
- Use `--rerun-tasks` only when troubleshooting cache issues
- For Kotest, the runner is registered with JUnit Platform — the standard `./gradlew test` and `--tests` flags work the same way

## Lint Command

Use the repo's lint tooling first:

- `./gradlew ktlintCheck` (autoformat: `./gradlew ktlintFormat`) when ktlint is configured
- `./gradlew detekt` when detekt is configured
- `./gradlew spotlessCheck` / `spotlessApply` for the Spotless plugin
- Android Studio's IDE inspections; `./gradlew lint` (Android-only) for the Android Lint task

## Project Layout

```
src/
├── main/kotlin/com/example/foo/Bar.kt
├── main/resources/
├── test/kotlin/com/example/foo/BarTest.kt   # mirrors production package
└── test/resources/
```

KMP layout:

```
src/
├── commonMain/kotlin/...        # shared
├── commonTest/kotlin/...        # shared tests using kotlin.test
├── jvmMain/kotlin/...
├── jvmTest/kotlin/...
├── jsMain/kotlin/...
└── jsTest/kotlin/...
```

- Test classes mirror the production class's package so they can access `internal` members (Kotlin's `internal` is module-scoped — within the same Gradle module, including the test source set)
- For KMP common tests, you can only import from `kotlin.test` and other multiplatform-aware libraries (e.g. `kotlinx.coroutines.test`, Kotest multiplatform, MockK on JVM only)

## Test Framework Detection

| Dependency | Framework | Annotations / DSL |
|------------|-----------|--------------------|
| `org.jetbrains.kotlin:kotlin-test` | kotlin.test (multiplatform) | `@Test`, `@BeforeTest`, `assertEquals`, `assertFailsWith` |
| `junit-jupiter-*` | JUnit 5 | `@Test`, `@ParameterizedTest`, `@BeforeEach`, `@DisplayName` |
| `junit:junit:4.x` | JUnit 4 | `@Test`, `@Before`, `@RunWith(JUnitPlatform::class)` rare |
| `io.kotest:kotest-runner-junit5` | Kotest | `class FooSpec : FunSpec({ test("...") { ... } })` (DSL — many styles: `StringSpec`, `BehaviorSpec`, etc.) |
| `org.spekframework.spek2:spek-dsl-jvm` | Spek 2 | `object FooSpec : Spek({ describe(...) { it(...) {} } })` (legacy) |

For Kotest, **stick to the spec style the repo already uses** — mixing styles is confusing.

## Test Templates

### JUnit 5

```kotlin
package com.example.foo

import org.junit.jupiter.api.DisplayName
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows
import kotlin.test.assertEquals

class CalculatorTest {

    @Test
    @DisplayName("add returns sum of two positive numbers")
    fun `add returns sum of two positives`() {
        val sut = Calculator()
        assertEquals(5, sut.add(2, 3))
    }

    @Test
    fun `divide by zero throws`() {
        val sut = Calculator()
        assertThrows<ArithmeticException> { sut.divide(1, 0) }
    }
}
```

Backticked method names (`` `like this` ``) are idiomatic for Kotlin tests because they read better in failure messages.

### Kotest (StringSpec)

```kotlin
package com.example.foo

import io.kotest.core.spec.style.StringSpec
import io.kotest.matchers.shouldBe
import io.kotest.assertions.throwables.shouldThrow

class CalculatorSpec : StringSpec({
    "add returns sum of two positive numbers" {
        Calculator().add(2, 3) shouldBe 5
    }

    "divide by zero throws" {
        shouldThrow<ArithmeticException> { Calculator().divide(1, 0) }
    }
})
```

## Coroutines

- Use `kotlinx-coroutines-test` when it's already on the classpath; otherwise add it as a `testImplementation` only after confirming it is missing (see Dependency Installation)
- Use `runTest { ... }` (replaces the older `runBlockingTest`) for `suspend` test bodies
- For virtual time advance, use a `TestDispatcher` built from `testScheduler` — e.g. `StandardTestDispatcher(testScheduler)` or `UnconfinedTestDispatcher(testScheduler)` — rather than calling `delay` and waiting in real time
- Inject a `CoroutineDispatcher` into production code instead of using `Dispatchers.Main/IO` directly — then swap it in tests via `Dispatchers.setMain(testDispatcher)`

```kotlin
@Test
fun `loads data eventually`() = runTest {
    val repo = FakeRepo()
    val dispatcher = StandardTestDispatcher(testScheduler)
    val sut = Loader(repo, dispatcher)
    sut.start()
    advanceUntilIdle()
    assertEquals(LoadState.Done, sut.state.value)
}
```

## Common Errors

| Error | Fix |
|-------|-----|
| `Unresolved reference: X` | Add the import; verify the test source set sees the production source set; for KMP, the dep may be declared only in `jvmTest` |
| `Cannot access 'X': it is internal in module Y` | `internal` is module-scoped, so a test in another Gradle module cannot see it. Move the test into the same module, expose a public seam (e.g. a `*-testing` artifact, or change visibility deliberately), or add the consuming module to the source module's `friend modules` via the Kotlin compiler `-Xfriend-paths` option. `@VisibleForTesting` does **not** widen Kotlin visibility |
| `Class 'XTest' is not abstract and does not implement abstract member` (Kotest spec) | The spec class needs a no-arg constructor and a primary-constructor block — match the existing spec style |
| `No tests found for given includes` (Gradle) | `--tests` pattern doesn't match; verify class name and that the framework's runner is registered on the test task (`useJUnitPlatform()`) |
| `kotlin.UninitializedPropertyAccessException: lateinit property X has not been initialized` | The `@BeforeEach` (or `BeforeTest`) didn't run, or the field was reset; use `lateinit` only after confirming the lifecycle hook fires |
| `IllegalStateException: Module with the Main dispatcher had failed to initialize` | Coroutines test needs `Dispatchers.setMain(...)` before launching anything that touches `Dispatchers.Main`; reset with `Dispatchers.resetMain()` in teardown |
| `Mockito cannot mock final class` | Kotlin classes are `final` by default — either use **MockK** (works with final classes) or apply the `kotlin-allopen` plugin scoped to a marker annotation |
| `MissingMockKException` | The mock wasn't initialized; call `MockKAnnotations.init(this)` or use `@MockK` with `@MockKExtension` (JUnit 5) |
| KMP common test references a JVM-only API | Move the test to `jvmTest`, or use `expect/actual` declarations |
| Android: `Method ... not mocked` | The unit test runs on the JVM and the SDK class is just a stub — either use Robolectric, move the test to instrumented (`androidTest`), or refactor to inject the dependency |

## Mocking Rules

- **MockK** is the de-facto standard for Kotlin (final classes, coroutine support): `every { mock.foo() } returns 1`, `coEvery { mock.suspendFn() } returns 1`, `verify { mock.foo() }`, `coVerify { ... }`
- Mockito works on Kotlin too with `mockito-kotlin` extensions, but Kotlin classes are `final` by default — use Mockito's inline mock maker (default in Mockito 5+; the `mockito-inline` artifact for Mockito 3.x/4.x). `mockito-subclass` cannot mock final classes
- Avoid `mockkStatic`/`mockkObject` for production code you control — refactor to a wrapper instead
- Prefer constructor injection so you don't need framework annotations (`@InjectMocks`) at all
- If a test needs more than 3 mocks, flag it as a design smell

## Android Specifics

- Robolectric tests live under `src/test/` and emulate the Android framework on the JVM — fast but imperfect
- Instrumented tests live under `src/androidTest/`, require a connected device/emulator, and are slow — use sparingly
- Compose UI tests use `createComposeRule()` and `composeTestRule.onNodeWithText(...).performClick()` — match the existing test setup if Compose is in the project
- Hilt: use `@HiltAndroidTest` and `HiltAndroidRule` for instrumented tests; for unit tests pass fakes directly to ViewModels

## Dependency Installation (Last Resort)

Only add dependencies after investigation confirms they are missing.

`build.gradle.kts`:

```kotlin
dependencies {
    testImplementation("org.junit.jupiter:junit-jupiter:5.10.2")
    testImplementation("io.mockk:mockk:1.13.10")
    testImplementation("org.jetbrains.kotlinx:kotlinx-coroutines-test:1.8.0")
}

tasks.test {
    useJUnitPlatform()
}
```

If the repo uses a version catalog, add to `gradle/libs.versions.toml` and reference via `libs.junit.jupiter` etc. Match the major versions already in use.

## Skip Coverage Tools

Do not configure or run coverage tools (JaCoCo, Kover). Coverage is measured separately by the evaluation harness.
