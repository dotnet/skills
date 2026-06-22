# C++ Extension

Language-specific guidance for C++ test generation.

## Rule #1: Investigate the Repo First

Before writing any test or running any command, read:

1. **Existing tests** — find `*_test.cpp`, `*_tests.cpp`, `test_*.cc`, `tests/`, `test/`, `unittests/`, and any CTest/Bazel/Make test targets. Copy the framework, naming, fixtures, assertions, and helper style already in use.
2. **Build configuration** — inspect `CMakeLists.txt`, `CMakePresets.json`, `Makefile`, `WORKSPACE`, `MODULE.bazel`, `BUILD`, `BUILD.bazel`, `meson.build`, or CI scripts before inventing commands.
3. **Dependencies** — detect GoogleTest, GoogleMock, Catch2, doctest, Boost.Test, or a custom harness from package manifests and target links.
4. **Compiler and standard** — identify `CMAKE_CXX_STANDARD`, `-std=c++NN`, toolchain files, compiler wrappers, sanitizers, and warning-as-error flags.
5. **Coverage setup** — determine whether the repo already has `gcov`, `llvm-cov`, `gcovr`, `lcov`, or custom coverage targets. If not, add minimal, test-target-scoped coverage instrumentation.

Generated C++ tests must earn coverage, not merely compile. Target uncovered functions, branches, error paths, and boundary conditions; shallow smoke tests that only construct objects rarely move line coverage.

## Environment and Toolchain Detection

| Indicator | Meaning |
|-----------|---------|
| `CMakeLists.txt` | CMake project; prefer configured build directories and existing presets |
| `CMakePresets.json` | Use `cmake --preset <name>` and `cmake --build --preset <name>` when present |
| `Makefile` without CMake | Use `make`, `make test`, or the repo's documented targets |
| `WORKSPACE`, `MODULE.bazel`, `BUILD(.bazel)` | Bazel project; tests are `cc_test` targets |
| `compile_commands.json` | Exact compiler flags and include directories used by the build |
| `CMAKE_CXX_STANDARD` / `-std=c++17` | Maximum language features allowed in tests |
| `clang++` | Prefer `llvm-cov gcov` for coverage data collection |
| `g++` | Use matching `gcov` from the same GCC toolchain version |

Useful discovery commands:

```bash
cmake --version
g++ --version
clang++ --version
find . \( -name CMakeLists.txt -o -name CMakePresets.json -o -name Makefile -o -name BUILD -o -name BUILD.bazel \)
find . \( -name '*test*.cpp' -o -name '*test*.cc' -o -name '*tests*.cpp' -o -name '*tests*.cc' \)
```

Do not change the project's C++ standard to make a test compile. Match the production target's standard and include directories.

## Test Framework Detection

| Indicator | Framework |
|-----------|-----------|
| `find_package(GTest)` / `GTest::gtest_main` / `gtest_discover_tests` | GoogleTest |
| `GTest::gmock` / `#include <gmock/gmock.h>` | GoogleMock for mocks |
| `find_package(Catch2 3)` / `Catch2::Catch2WithMain` / `catch_discover_tests` | Catch2 v3 |
| `#include <catch2/catch_test_macros.hpp>` | Catch2 v3 test source |
| `#include <gtest/gtest.h>` | GoogleTest test source |
| `add_test(NAME ... COMMAND ...)` | Manual CTest registration |
| `cc_test(` | Bazel C++ test target |

Use the framework already present. Do not add Catch2 to a GoogleTest repo or GoogleTest to a Catch2 repo just because it is familiar.

## Build Commands

Prefer repo scripts and presets first. Otherwise use the smallest command that compiles the changed test target.

| Scope | CMake command |
|-------|---------------|
| Configure debug build | `cmake -S . -B build -DCMAKE_BUILD_TYPE=Debug` |
| Configure with tests | `cmake -S . -B build -DCMAKE_BUILD_TYPE=Debug -DBUILD_TESTING=ON` |
| Configure with coverage option | `cmake -S . -B build-coverage -DCMAKE_BUILD_TYPE=Debug -DBUILD_TESTING=ON -DCODE_COVERAGE=ON` |
| Build all | `cmake --build build` |
| Build one test target | `cmake --build build --target my_component_tests` |
| Parallel build | `cmake --build build --parallel` |
| Clean rebuild | `cmake --build build --target clean && cmake --build build` |

Make equivalents when no CMake build exists:

| Scope | Make command |
|-------|--------------|
| Build all | `make` |
| Build tests | `make test` or `make tests` |
| Build one target | `make my_component_tests` |
| Coverage build | `make clean && CXXFLAGS="--coverage -O0 -g" LDFLAGS="--coverage" make tests` |

Bazel equivalents:

| Scope | Bazel command |
|-------|---------------|
| Build tests | `bazel build //...` |
| Run all tests | `bazel test //...` |
| Run one test | `bazel test //path/to:target_test` |

## Test Commands

| Scope | Command |
|-------|---------|
| All CTest tests | `ctest --test-dir build --output-on-failure` |
| Verbose CTest failure | `ctest --test-dir build --output-on-failure -V` |
| One CTest test by regex | `ctest --test-dir build -R my_component --output-on-failure` |
| Direct GoogleTest binary | `./build/tests/my_component_tests` |
| GoogleTest filter | `./build/tests/my_component_tests --gtest_filter=SuiteName.TestName` |
| GoogleTest list tests | `./build/tests/my_component_tests --gtest_list_tests` |
| Direct Catch2 binary | `./build/tests/my_component_tests` |
| Catch2 filter by name | `./build/tests/my_component_tests "test case name"` |
| Catch2 filter by tag | `./build/tests/my_component_tests "[parser]"` |
| Catch2 list tests | `./build/tests/my_component_tests --list-tests` |

For CTest, run from the configured build tree or pass `--test-dir build`; running `ctest` from the source root often reports zero tests.

## Lint Command

Use the repo's lint script first (`make lint`, `cmake --build build --target lint`, `ninja clang-tidy`). Otherwise detect tools from configuration:

- `.clang-format` present → `clang-format -i path/to/test.cpp`
- `.clang-tidy` present → run the repo's clang-tidy target or `clang-tidy path/to/test.cpp -- -Iinclude`
- CMake format target present → `cmake --build build --target format`

Never silence warnings in generated tests with blanket pragmas. Fix the warning or match the existing project pattern.

## Project Layout and Test File Placement

Common layouts:

```text
project/
├── include/              # public headers
├── src/                  # implementation files
├── tests/                # test sources and CMakeLists.txt
│   ├── CMakeLists.txt
│   └── widget_test.cpp
├── CMakeLists.txt
└── CMakePresets.json
```

| Source file | Preferred test file |
|-------------|---------------------|
| `src/widget.cpp` | `tests/widget_test.cpp` or existing repo pattern |
| `src/parser/tokenizer.cpp` | `tests/parser/tokenizer_test.cpp` |
| `include/lib/widget.hpp` | `tests/widget_test.cpp` using the public API |

- Match existing suffixes: `_test.cpp`, `_tests.cpp`, `test_*.cpp`, or `.cc`.
- Keep tests near existing test CMake targets instead of creating an isolated harness.
- Prefer testing through public headers. Use white-box access only when existing tests already do so or coverage-critical internals cannot be reached otherwise.
- Shared fixtures/helpers belong in `tests/support/`, `tests/helpers/`, or the existing helper location, not production `src/` unless the repo already has test-only utilities.

## GoogleTest Setup

Minimal test source:

```cpp
#include <gtest/gtest.h>

#include "calculator.hpp"

TEST(CalculatorTest, Add_WithPositiveInputs_ReturnsSum) {
    Calculator calculator;

    EXPECT_EQ(calculator.Add(2, 3), 5);
}
```

CMake registration with discovery:

```cmake
enable_testing()
find_package(GTest REQUIRED)
include(GoogleTest)

add_executable(calculator_tests
    tests/calculator_test.cpp
)
target_link_libraries(calculator_tests
    PRIVATE
        calculator_lib
        GTest::gtest_main
)
gtest_discover_tests(calculator_tests)
```

If the repo already has a test helper function such as `add_project_test(...)`, use it instead of writing raw `add_executable` blocks.

## Catch2 v3 Setup

Minimal test source:

```cpp
#include <catch2/catch_test_macros.hpp>

#include "calculator.hpp"

TEST_CASE("Calculator adds positive inputs", "[calculator]") {
    Calculator calculator;

    CHECK(calculator.Add(2, 3) == 5);
}
```

CMake registration with discovery:

```cmake
enable_testing()
find_package(Catch2 3 REQUIRED)
include(Catch)

add_executable(calculator_tests
    tests/calculator_test.cpp
)
target_link_libraries(calculator_tests
    PRIVATE
        calculator_lib
        Catch2::Catch2WithMain
)
catch_discover_tests(calculator_tests)
```

Use `Catch2::Catch2WithMain` unless the repo already provides a custom `main`. Linking a framework main and defining your own `main` causes duplicate-symbol linker failures.

## Coverage Instrumentation

Coverage instrumentation is the highest-risk setup step. Add coverage flags to **both compilation and linking** for the test target. Adding flags only to `CXXFLAGS` often compiles but produces no `.gcda` files or fails with missing gcov runtime symbols at link time.

### CMake target-scoped coverage option

Prefer target-scoped flags over global `CMAKE_CXX_FLAGS` so production targets stay clean:

```cmake
option(CODE_COVERAGE "Build tests with gcov-compatible coverage instrumentation" OFF)

add_executable(calculator_tests
    tests/calculator_test.cpp
)
target_link_libraries(calculator_tests
    PRIVATE
        calculator_lib
        GTest::gtest_main
)

if(CODE_COVERAGE AND CMAKE_CXX_COMPILER_ID MATCHES "GNU|Clang|AppleClang")
    target_compile_options(calculator_tests PRIVATE --coverage -O0 -g)
    target_link_options(calculator_tests PRIVATE --coverage)
endif()
```

For libraries built into the test binary, instrument the library target too, otherwise coverage only reports test files:

```cmake
if(CODE_COVERAGE AND CMAKE_CXX_COMPILER_ID MATCHES "GNU|Clang|AppleClang")
    target_compile_options(calculator_lib PRIVATE --coverage -O0 -g)
    target_link_options(calculator_lib PRIVATE --coverage)
    target_compile_options(calculator_tests PRIVATE --coverage -O0 -g)
    target_link_options(calculator_tests PRIVATE --coverage)
endif()
```

Equivalent long-form flags:

```cmake
target_compile_options(calculator_tests PRIVATE -fprofile-arcs -ftest-coverage -O0 -g)
target_link_options(calculator_tests PRIVATE -fprofile-arcs -ftest-coverage)
```

### Coverage command sequence

GCC/gcov path:

```bash
cmake -S . -B build-coverage -DCMAKE_BUILD_TYPE=Debug -DBUILD_TESTING=ON -DCODE_COVERAGE=ON
cmake --build build-coverage --target calculator_tests
ctest --test-dir build-coverage --output-on-failure
gcovr --root . --filter 'src/' --filter 'include/' --exclude 'tests/' --print-summary
```

Clang path using gcov-compatible data:

```bash
cmake -S . -B build-coverage -DCMAKE_CXX_COMPILER=clang++ -DCMAKE_BUILD_TYPE=Debug -DBUILD_TESTING=ON -DCODE_COVERAGE=ON
cmake --build build-coverage --target calculator_tests
ctest --test-dir build-coverage --output-on-failure
gcovr --root . --gcov-executable 'llvm-cov gcov' --filter 'src/' --filter 'include/' --exclude 'tests/' --print-summary
```

`lcov` / `genhtml` path:

```bash
cmake -S . -B build-coverage -DCMAKE_BUILD_TYPE=Debug -DBUILD_TESTING=ON -DCODE_COVERAGE=ON
cmake --build build-coverage
ctest --test-dir build-coverage --output-on-failure
lcov --capture --directory build-coverage --output-file coverage.info
lcov --remove coverage.info '*/tests/*' '*/_deps/*' --output-file coverage.filtered.info
genhtml coverage.filtered.info --output-directory coverage-html
```

Make-only coverage path:

```bash
make clean
CXXFLAGS="--coverage -O0 -g" LDFLAGS="--coverage" make tests
./tests/calculator_tests
gcovr --root . --print-summary
```

Important coverage rules:

- Compile and link with the same compiler family. Clang-generated coverage data should be read with `llvm-cov gcov`, not system `gcov`.
- Run the instrumented test binary before collecting; `.gcda` files are written when the process exits normally.
- Use `-O0 -g` for coverage builds to keep line mapping stable.
- Exclude test files and vendored dependencies from coverage reports; include production `src/` and `include/`.
- If the code under test is a static library, object library, or source list linked into tests, instrument that target as well as the test executable.

## Coverage-Targeting Guidance

To avoid low coverage deltas:

1. Read the coverage report and identify uncovered production files, functions, and branch lines.
2. Write tests that drive real behavior through public APIs or stable seams.
3. Prioritize branches: error handling, empty input, boundary values, invalid parse cases, feature flags, and state transitions.
4. Prefer one parameterized test that covers many meaningful paths over many tests that repeat the same happy path.
5. Assert observable outcomes, side effects, return codes, exceptions, and mock interactions. A test that only constructs an object usually adds little or no useful coverage.
6. Re-run the targeted coverage command and confirm the intended files moved.

Do not chase coverage by testing implementation details that make the suite brittle when a public API can cover the same lines.

## Test Patterns

### GoogleTest assertions

| Need | Pattern |
|------|---------|
| Non-fatal equality | `EXPECT_EQ(actual, expected)` |
| Fatal precondition | `ASSERT_NE(pointer, nullptr)` |
| Boolean | `EXPECT_TRUE(value)` / `EXPECT_FALSE(value)` |
| String equality | `EXPECT_STREQ(actual.c_str(), "expected")` |
| Floating point | `EXPECT_NEAR(actual, expected, 1e-6)` |
| Exception | `EXPECT_THROW(call(), std::invalid_argument)` |
| No exception | `EXPECT_NO_THROW(call())` |

Use `ASSERT_*` only when the rest of the test cannot safely continue.

### GoogleTest fixtures and parameterized tests

```cpp
class ParserTest : public ::testing::Test {
protected:
    Parser parser_;
};

TEST_F(ParserTest, Parse_WithEmptyInput_ReturnsEmptyResult) {
    EXPECT_TRUE(parser_.Parse("").empty());
}

class ClampTest : public ::testing::TestWithParam<std::tuple<int, int, int, int>> {};

TEST_P(ClampTest, Clamp_WithBoundaryInputs_ReturnsExpectedValue) {
    const auto [value, min, max, expected] = GetParam();

    EXPECT_EQ(Clamp(value, min, max), expected);
}

INSTANTIATE_TEST_SUITE_P(
    BoundaryCases,
    ClampTest,
    ::testing::Values(
        std::make_tuple(-1, 0, 10, 0),
        std::make_tuple(5, 0, 10, 5),
        std::make_tuple(11, 0, 10, 10)));
```

### Catch2 assertions and generators

```cpp
#include <catch2/catch_approx.hpp>
#include <catch2/catch_test_macros.hpp>
#include <catch2/generators/catch_generators.hpp>

TEST_CASE("Clamp handles boundary inputs", "[math]") {
    const auto [value, min, max, expected] = GENERATE(
        std::tuple{-1, 0, 10, 0},
        std::tuple{5, 0, 10, 5},
        std::tuple{11, 0, 10, 10});

    CHECK(Clamp(value, min, max) == expected);
}

TEST_CASE("Divide rejects zero denominator", "[math]") {
    REQUIRE_THROWS_AS(Divide(1.0, 0.0), std::invalid_argument);
    CHECK(Divide(1.0, 3.0) == Catch::Approx(0.333333).epsilon(0.001));
}
```

Use `REQUIRE` when execution must stop after failure; use `CHECK` for independent assertions.

## Mocking Rules

Use GoogleMock when the repo already uses gMock or GoogleTest with mocks:

```cpp
#include <gmock/gmock.h>

class MockClock : public Clock {
public:
    MOCK_METHOD(std::chrono::seconds, Now, (), (const, override));
};

TEST(SchedulerTest, ShouldRun_WhenIntervalElapsed_ReturnsTrue) {
    MockClock clock;
    EXPECT_CALL(clock, Now()).WillOnce(::testing::Return(std::chrono::seconds{42}));

    Scheduler scheduler(clock);

    EXPECT_TRUE(scheduler.ShouldRun());
}
```

Guidelines:

- Mock interfaces with virtual methods and virtual destructors.
- Prefer small interfaces or constructor injection over global state.
- For code without virtuals, create seams with templates, function objects, adapters, or thin interfaces around external dependencies.
- Do not mock standard library containers or value objects; build real values.
- If a test needs more than three mocks, treat it as a design smell and look for a higher-level behavioral test.

## Testing Internals

If types are not well suited for testing only through their public surface, consider exposing internals to tests using a preprocessor-guarded `friend` declaration:

```cpp
class MyClass {
#ifdef UNIT_TESTING
    friend class MyClassTest;
#endif
    // ...
};
```

Define `UNIT_TESTING` only in the test build configuration so production builds remain unaffected:

```cmake
target_compile_definitions(my_component_tests PRIVATE UNIT_TESTING)
```

Use this sparingly. Prefer public behavior tests and dependency seams before adding test-only friendship.

## Common Errors

| Error | Fix |
|-------|-----|
| `undefined reference to __gcov_init` / `__gcov_exit` | Add `--coverage` or `-fprofile-arcs -ftest-coverage` to link flags, not only compile flags |
| No `.gcda` files produced | Ensure the instrumented binary ran to normal exit, the production target was instrumented, and the build directory is writable |
| `profiling: ... cannot merge previous GCDA file` | Delete old coverage files or rebuild clean after changing compiler/options |
| `gcov: stamp mismatch` | Clean the build directory; `.gcno` and `.gcda` came from different builds |
| Clang coverage unreadable by `gcov` | Run `gcovr --gcov-executable 'llvm-cov gcov'` |
| `multiple definition of main` | Link `GTest::gtest_main` or `Catch2::Catch2WithMain`, or provide your own main, not both |
| CTest reports `No tests were found!!!` | Add `enable_testing()` and `gtest_discover_tests`, `catch_discover_tests`, or `add_test`; run `ctest --test-dir build` |
| `fatal error: gtest/gtest.h: No such file or directory` | Use the repo's dependency mechanism or add `find_package(GTest REQUIRED)` / FetchContent only as last resort |
| `undefined reference` to production symbols | Link the test target to the library under test with `target_link_libraries(test PRIVATE my_lib)` |
| ABI or standard mismatch | Match `CMAKE_CXX_STANDARD`, compiler, runtime, and flags between production and test targets |
| `EXPECT_EQ` prints unreadable custom types | Add `operator==` and, if useful, `operator<<` in the test namespace or production type namespace |
| Test passes directly but not under CTest | Check working directory assumptions; set `WORKING_DIRECTORY` in `add_test` or use paths relative to test data |
| Segfault in test cleanup | Avoid owning raw pointers in tests; use RAII objects and make mock lifetimes outlive the object under test |

## Dependency Installation (Last Resort)

Only add dependencies after investigation confirms the repo has no test framework or the expected framework is missing. Prefer existing package managers and lockfiles.

CMake with installed packages:

```cmake
find_package(GTest REQUIRED)
find_package(Catch2 3 REQUIRED)
```

CMake FetchContent fallback for GoogleTest:

```cmake
include(FetchContent)
FetchContent_Declare(
    googletest
    URL https://github.com/google/googletest/archive/refs/tags/v1.14.0.zip)
FetchContent_MakeAvailable(googletest)
```

CMake FetchContent fallback for Catch2 v3:

```cmake
include(FetchContent)
FetchContent_Declare(
    Catch2
    URL https://github.com/catchorg/Catch2/archive/refs/tags/v3.5.4.zip)
FetchContent_MakeAvailable(Catch2)
list(APPEND CMAKE_MODULE_PATH ${catch2_SOURCE_DIR}/extras)
```

Package manager examples:

| Manager | Command |
|---------|---------|
| vcpkg | `vcpkg install gtest catch2` |
| Conan | `conan install . --build=missing` |
| apt | `sudo apt-get install libgtest-dev catch2 gcovr lcov` |
| Homebrew | `brew install googletest catch2 gcovr lcov` |

On Debian/Ubuntu, `libgtest-dev` historically installs only GoogleTest *sources* (no prebuilt libraries or CMake package config), so `find_package(GTest REQUIRED)` can still fail. On those systems either build/install GoogleTest from the packaged sources, add it via `FetchContent`, or use vcpkg/Conan instead of relying on apt alone.

Do not vendor dependencies by copying source into the repo unless that is already the project's dependency policy.

## Skip

Skip or avoid these actions unless the repo explicitly requires them:

- Do not replace the build system or create a parallel test harness outside CMake/Make/Bazel just for generated tests.
- Do not add coverage flags globally to release builds; keep coverage in a Debug/test-only configuration.
- Do not use system `gcov` with Clang-generated coverage data.
- Do not define a second test `main` when linking framework-provided main targets.
- Do not write tests that only instantiate objects without assertions or behavior coverage.
- Do not add a new test framework when an existing one is already configured.
