# C++ Test Frameworks Reference (GoogleTest, Catch2, doctest, Boost.Test)

Reference data for analyzing C++ test code. Used by the polyglot test analysis skills.

## Capability tags

| Capability | Support |
|------------|---------|
| Test discovery | Strong — `TEST*` macros, `TEST_CASE`, `DOCTEST_TEST_CASE` |
| Assertion detection | Strong — `ASSERT_*`, `EXPECT_*`, `REQUIRE`, `CHECK` |
| Sleep/delay detection | Strong — `std::this_thread::sleep_for`, `sleep()`, `Sleep()` |
| Skip/ignore detection | Moderate — `GTEST_SKIP()`, `DISABLED_` prefix, `[!hide]` tags |
| Setup/teardown detection | Strong — `SetUp`/`TearDown`, fixtures, sections |
| Tag support | **auto-edit** — Catch2 uses `[tag]` syntax inside `TEST_CASE`; doctest uses `* doctest::test_suite("tag")` decorator chains; GoogleTest uses test-name prefix conventions (treat as `convention-based`) |

## Test File Identification

| Framework | File convention | Test method markers |
|-----------|----------------|---------------------|
| GoogleTest | `*_test.cc/cpp`, `*Tests.cpp` | `TEST(SuiteName, TestName)`, `TEST_F(FixtureClass, TestName)`, `TEST_P(...)` parametrized, `TYPED_TEST(...)` |
| Catch2 | `*Tests.cpp`, `test*.cpp` | `TEST_CASE("name", "[tags]")`, `SCENARIO`, `SECTION` |
| doctest | `*Tests.cpp` | `TEST_CASE("name" * doctest::test_suite("suite"))` |
| Boost.Test | `*_test.cpp` | `BOOST_AUTO_TEST_CASE(name)`, `BOOST_FIXTURE_TEST_CASE(name, Fixture)` |

## Assertion APIs

| Category | GoogleTest | Catch2 | doctest | Boost.Test |
|----------|------------|--------|---------|------------|
| Equality (continue) | `EXPECT_EQ(actual, expected)` | `CHECK(actual == expected)` | `CHECK(actual == expected)` | `BOOST_CHECK_EQUAL(actual, expected)` |
| Equality (abort) | `ASSERT_EQ(actual, expected)` | `REQUIRE(actual == expected)` | `REQUIRE(actual == expected)` | `BOOST_REQUIRE_EQUAL(actual, expected)` |
| Boolean | `EXPECT_TRUE(x)` / `EXPECT_FALSE(x)` | `CHECK(x)` / `CHECK_FALSE(x)` | `CHECK(x)` | `BOOST_CHECK(x)` |
| Null/Pointer | `EXPECT_EQ(ptr, nullptr)` | `CHECK(ptr == nullptr)` | `CHECK(ptr == nullptr)` | `BOOST_CHECK(ptr == nullptr)` |
| Throws | `EXPECT_THROW(stmt, ExType)` / `EXPECT_THROW(stmt, std::exception)` | `CHECK_THROWS_AS(expr, ExType)` / `CHECK_THROWS_WITH(expr, "...")` / `CHECK_THROWS_MATCHES(...)` | `CHECK_THROWS_AS(expr, ExType)` | `BOOST_CHECK_THROW(expr, ExType)` |
| No throw | `EXPECT_NO_THROW(stmt)` | `CHECK_NOTHROW(expr)` | `CHECK_NOTHROW(expr)` | `BOOST_CHECK_NO_THROW(expr)` |
| Approximate | `EXPECT_NEAR(a, b, abs_err)` / `EXPECT_DOUBLE_EQ(a, b)` | `CHECK(actual == Approx(expected))` | `CHECK(actual == doctest::Approx(expected))` | `BOOST_CHECK_CLOSE(a, b, tol_pct)` |
| String | `EXPECT_STREQ(c_str_a, c_str_b)` / `EXPECT_THAT(s, HasSubstr("x"))` | `CHECK(s.find("x") != std::string::npos)` | similar | `BOOST_CHECK_EQUAL(s, expected)` |
| Death tests | `EXPECT_DEATH(stmt, "regex")` / `EXPECT_EXIT(...)` | n/a | n/a | n/a |
| Custom matchers | `EXPECT_THAT(value, gmock_matchers::Eq(x))` | `REQUIRE_THAT(value, Catch::Matchers::Equals(x))` | similar | n/a |

**EXPECT vs ASSERT/REQUIRE vs CHECK:**
- GoogleTest: `EXPECT_*` continues on failure; `ASSERT_*` aborts the test.
- Catch2 / doctest: `CHECK*` continues; `REQUIRE*` aborts.
- Boost.Test: `BOOST_CHECK*` continues; `BOOST_REQUIRE*` aborts.

## Sleep/Delay Patterns

| Pattern | Example |
|---------|---------|
| C++11 thread sleep | `std::this_thread::sleep_for(std::chrono::seconds(1))` |
| POSIX sleep | `sleep(1);` / `usleep(500000);` |
| Windows sleep | `Sleep(1000);` |
| Loop wait | `while (!ready) std::this_thread::sleep_for(...)` |
| Async wait (acceptable) | `future.wait_for(std::chrono::seconds(5))` |

## Skip/Ignore Annotations

| Framework | Mechanism |
|-----------|-----------|
| GoogleTest | `GTEST_SKIP() << "reason";` inside test body; test name prefix `DISABLED_` (e.g., `TEST(F, DISABLED_X)`) |
| Catch2 | `[!hide]` or `[.]` tag in `TEST_CASE("name", "[.]")`; `SUCCEED("skipped")` |
| doctest | `* doctest::skip()` decorator: `TEST_CASE("name" * doctest::skip(true))` |
| Boost.Test | `boost::unit_test::disabled()` decorator, or `BOOST_AUTO_TEST_CASE(name, *boost::unit_test::disabled())` |

`DISABLED_` prefix without a tracking comment is a smell — flag as Ignored Test.

## Exception Handling — Idiomatic Alternatives

```cpp
// GoogleTest:
EXPECT_THROW({
    service.placeOrder(empty);
}, InvalidOrderException);

// Or capture and inspect:
try {
    service.placeOrder(empty);
    FAIL() << "Expected InvalidOrderException";
} catch (const InvalidOrderException& e) {
    EXPECT_STREQ("at least one item", e.what());
}

// Catch2:
REQUIRE_THROWS_AS(service.placeOrder(empty), InvalidOrderException);
REQUIRE_THROWS_WITH(service.placeOrder(empty), Catch::Contains("at least one item"));
```

The manual try/catch/FAIL pattern is acceptable when message inspection is needed; flag bare `try { ... } catch (...) {}` (swallowed).

## Mystery Guest — Common C++ Patterns

| Indicator | What to look for |
|-----------|------------------|
| File system | `std::ifstream`, `std::ofstream`, `fopen`, hard-coded paths |
| Network | raw `socket()` / `connect()`, `curl_easy_perform` to real URL |
| Environment | `std::getenv("X")`, Windows registry calls |
| Database | direct `sqlite3_open(path)`, ODBC connections |
| Acceptable | `std::stringstream`, `std::tmpfile`, GoogleMock for collaborators, `boost::iostreams`, in-memory streams |

## Integration Test Markers

- File suffix: `*_integration_test.cc`, `*_e2e_test.cpp`
- GoogleTest suite names containing `Integration` / `EndToEnd`
- Catch2 tags: `[integration]`, `[e2e]`, `[slow]`
- CMake target names ending in `_integration_tests`
- Conditional compilation: `#ifdef BUILD_INTEGRATION_TESTS`

## Setup/Teardown

| Framework | Per-test | Per-suite |
|-----------|----------|-----------|
| GoogleTest fixture | `void SetUp() override` | `static void SetUpTestSuite()` |
| GoogleTest fixture | `void TearDown() override` | `static void TearDownTestSuite()` |
| Catch2 | `TEST_CASE` body + `SECTION` re-runs setup per section | fixture class via `TEST_CASE_METHOD(Fixture, "name")` |
| doctest | similar to Catch2 | `doctest::TestCase` fixture |
| Boost.Test | `BOOST_FIXTURE_TEST_CASE(name, Fixture)` | `BOOST_GLOBAL_FIXTURE(Fixture)` |

Catch2 `SECTION`s are re-entered for each combination, so the `TEST_CASE` body acts as fresh per-section setup — a powerful idiom.

## Tag/Trait Attributes (for `test-tagging`)

| Framework | Tag mechanism | Example |
|-----------|---------------|---------|
| Catch2 | `[tag]` syntax in `TEST_CASE` second arg | `TEST_CASE("creates order", "[positive][critical-path]")` |
| doctest | `* doctest::test_suite("tag")` decorator chain | `TEST_CASE("name" * doctest::test_suite("positive"))` |
| GoogleTest | test name prefix convention (e.g., `Positive_*`, `Boundary_*`) or `--gtest_filter` patterns | suite naming or `TEST(PositiveCases, ...)`; **report-only** for auto-edit |
| Boost.Test | label decorator: `* boost::unit_test::label("positive")` | `BOOST_AUTO_TEST_CASE(name, *boost::unit_test::label("positive"))` |

Filter syntax:
- Catch2: `./tests "[positive]" ~"[slow]"`
- doctest: `./tests -ts="positive"`
- GoogleTest: `./tests --gtest_filter='Positive*'`
- Boost.Test: `./tests --run_test=@positive`

## Language-specific calibration notes

- **`EXPECT_*` continues on failure** in GoogleTest — many `EXPECT_EQ` calls in one test may produce cascading messages from one root cause.
- **`REQUIRE_*` / `ASSERT_*` aborts** — use for preconditions in long tests.
- **Death tests** (`EXPECT_DEATH`) fork the process and check stderr — slow; acknowledge as integration-style.
- **`DISABLED_` prefix** disables tests silently — `--gtest_also_run_disabled_tests` is required to opt back in. Flag committed `DISABLED_` tests as Ignored Test.
- **Catch2 `SECTION`s** are NOT duplicate tests — each section is a permutation of the parent `TEST_CASE`.
- **GoogleMock `EXPECT_CALL(mock, Method(...))`** counts as a state/side-effect assertion.
- **Template / typed tests** (`TYPED_TEST`, `TEMPLATE_TEST_CASE`) are parametrized, not duplicates.
- **Hidden tests** (Catch2 `[.]` or `[!hide]`) are excluded by default but runnable on demand — note in audit.
- **Sanitizer-only tests** (`#ifdef __SANITIZE_THREAD__`, etc.) are conditional smoke checks — note but don't flag.
- **Test binaries that don't link `gtest_main`** require a custom `main()` — verify it calls `RUN_ALL_TESTS()`.
- **`SUCCEED()` / `INFO(...)`** are not assertions; tests with only `SUCCEED()` are assertion-free.
