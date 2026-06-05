# Swift Test Frameworks Reference (XCTest, Swift Testing)

Reference data for analyzing Swift test code. Used by the polyglot test analysis skills.

## Capability tags

| Capability | Support |
|------------|---------|
| Test discovery | Strong — `XCTestCase` subclasses, `@Test` functions |
| Assertion detection | Strong — `XCTAssert*`, `#expect`, `#require` |
| Sleep/delay detection | Strong — `Thread.sleep`, `Task.sleep`, `XCTWaiter` |
| Skip/ignore detection | Strong — `XCTSkip`, `.disabled(...)` |
| Setup/teardown detection | Strong — `setUp/tearDown`, `init/deinit` for Swift Testing |
| Tag support | **auto-edit** (Swift Testing) — `@Test(.tags(...))` / `@Suite(.tags(...))`; XCTest: report-only |

## Test File Identification

| Framework | File convention | Test method markers |
|-----------|----------------|---------------------|
| XCTest | `*Tests.swift` (Swift Package Manager: `Tests/<Module>Tests/`) | `class FooTests: XCTestCase` with methods starting `test` |
| Swift Testing | same conventions | `@Test func foo() async throws { ... }`, optionally inside `@Suite` types |

Both frameworks can coexist in one target.

## Assertion APIs

| Category | XCTest | Swift Testing |
|----------|--------|---------------|
| Equality | `XCTAssertEqual(actual, expected)` | `#expect(actual == expected)` |
| Inequality | `XCTAssertNotEqual` | `#expect(actual != expected)` |
| Boolean | `XCTAssertTrue` / `XCTAssertFalse` | `#expect(condition)` |
| Nil | `XCTAssertNil` / `XCTAssertNotNil` | `#expect(value == nil)` / `#expect(value != nil)` |
| Throws | `XCTAssertThrowsError(try fn()) { error in ... }` | `#expect(throws: SomeError.self) { try fn() }` / `try #require(throws: ...)` |
| No throw | `XCTAssertNoThrow(try fn())` | implicit (just call `try fn()`) |
| Identical (reference) | `XCTAssertIdentical` | `#expect(a === b)` |
| Approximate | `XCTAssertEqual(x, y, accuracy: 0.01)` | `#expect(abs(x - y) < 0.01)` |
| Type | `XCTAssertTrue(x is T)` | `#expect(x is T)` |
| Membership | `XCTAssertTrue(arr.contains(item))` | `#expect(arr.contains(item))` |
| Fail | `XCTFail("reason")` | `Issue.record("reason")` |
| Soft fail (continue) | continues on `XCTAssert*` by default | `#expect` (records issues, continues) |
| Hard fail (stop) | `XCTSkipIf` is skip; no hard-fail at test level | `try #require(...)` aborts the test |

## Sleep/Delay Patterns

| Pattern | Example |
|---------|---------|
| Thread sleep | `Thread.sleep(forTimeInterval: 1.0)` |
| Async sleep | `try await Task.sleep(nanoseconds: 1_000_000_000)` (Swift 5.5+) |
| Async sleep (newer) | `try await Task.sleep(for: .seconds(1))` (Swift 5.7+) |
| XCTest waiter | `wait(for: [exp], timeout: 5)` (acceptable for expectation-based tests) |
| Async waiter | `await fulfillment(of: [exp], timeout: 5)` (Xcode 14+) |

`XCTestExpectation` + `wait(for:timeout:)` is the idiomatic async-coordination pattern in XCTest — not a sleep smell.

## Skip/Ignore Annotations

| Framework | Annotation |
|-----------|------------|
| XCTest | `throw XCTSkip("reason")`, `XCTSkipIf(cond, "reason")`, `XCTSkipUnless(cond, "reason")` |
| Swift Testing | `@Test(.disabled("reason"))`, `@Test(.disabled(if: cond, "reason"))`, `@Test(.enabled(if: cond))` |

## Exception Handling — Idiomatic Alternatives

```swift
// XCTest:
XCTAssertThrowsError(try service.placeOrder(emptyOrder)) { error in
    guard case OrderError.empty = error else {
        XCTFail("Expected .empty, got \(error)")
        return
    }
}

// Swift Testing:
#expect(throws: OrderError.self) {
    try service.placeOrder(emptyOrder)
}

// Specific case (Swift Testing):
let err = try #require(throws: OrderError.self) { try service.placeOrder(emptyOrder) }
#expect(err == .empty)
```

Flag manual `do { try fn(); XCTFail("expected throw") } catch { ... }` patterns and recommend the framework-native form.

## Mystery Guest — Common Swift Patterns

| Indicator | What to look for |
|-----------|------------------|
| File system | `FileManager.default.contents(atPath:)`, hard-coded `Bundle.main` paths |
| Database | direct `SQLite.swift` against real file, raw `CoreData` saves outside in-memory store |
| Network | `URLSession.shared.dataTask` without `URLProtocol` stub, `Alamofire` to real URL |
| Environment | `ProcessInfo.processInfo.environment["X"]` |
| Acceptable | `URLProtocol` stubs, OHHTTPStubs, `Mocker`, `NSPersistentContainer` with in-memory store type, `Bundle.module` resource paths |

## Integration Test Markers

- Folder convention: `IntegrationTests/`, `UITests/`, `E2ETests/`
- Class name suffix: `*IntegrationTests`, `*UITests`
- `XCUITest` (`XCUIApplication`, `XCUIElement`) → UI/E2E test
- Swift Testing `@Tag` named `.integration` or `.ui`

## Setup/Teardown

| Framework | Per-test | Per-class/suite |
|-----------|----------|-----------------|
| XCTest | `setUp() / setUpWithError()` | `override class func setUp()` |
| XCTest | `tearDown() / tearDownWithError()` | `override class func tearDown()` |
| Swift Testing | `init(...) async throws` per instance | static via `@Suite` type |
| Swift Testing | `deinit` for cleanup | static via `@Suite` type |

Swift Testing creates a fresh instance per test by default — fields initialized in `init` are reset between tests.

## Tag/Trait Attributes (for `test-tagging`)

| Framework | Tag mechanism | Example |
|-----------|---------------|---------|
| Swift Testing | `@Test(.tags(.positive, .boundary))` (predefined or custom tag) | requires `extension Tag { @Tag static var positive: Self }` |
| Swift Testing (suite) | `@Suite(.tags(...))` | inherits tags to contained tests |
| XCTest | none built-in — use class organization, naming, or test plans (.xctestplan) | *(report-only)* |

For Swift Testing, define tags in a single module-level location:

```swift
extension Tag {
    @Tag static var positive: Self
    @Tag static var negative: Self
    @Tag static var boundary: Self
    @Tag static var integration: Self
}
```

## Language-specific calibration notes

- **Swift Testing `#expect` continues on failure**; `try #require` aborts. Tests that mix preconditions and assertions should use `try #require` for preconditions.
- **`XCTAssert*` continues on failure** — tests with multiple cascading assertions may log many failures from one root cause.
- **Async tests must `await`** — missing `await` causes warnings and silent skips on async APIs.
- **Combine tests** with `expectation(description:)` are XCTest's idiomatic async pattern; not a sleep smell.
- **Snapshot testing** (`SnapshotTesting` library) — treat snapshot comparisons as legitimate assertions; flag stale records.
- **Parametrized tests** (`@Test(arguments: [...])`) are NOT duplicates.
- **Test plans (`.xctestplan`)** can filter by tags / configurations; mention as a structural alternative to per-test tagging.
- **`continueAfterFailure`** — when `false`, XCTest stops on first failure (useful for fast-fail integration tests).
