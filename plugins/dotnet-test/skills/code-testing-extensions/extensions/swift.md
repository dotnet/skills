# Swift Extension

Language-specific guidance for Swift test generation.

## Rule #1: Investigate the Repo First

Before writing any test or running any command, read:

1. **Existing tests** — find files in `Tests/` (SPM) or `*Tests/` groups (Xcode) and copy their style. Distinguish **XCTest** (`import XCTest`, classes inheriting `XCTestCase`) from **Swift Testing** (`import Testing`, free functions tagged `@Test`)
2. **Project file** — `Package.swift` (SPM), `*.xcodeproj`, `*.xcworkspace`, or `Project.swift` (Tuist)
3. **Swift toolchain** — `.swift-version`, `swift-tools-version` line in `Package.swift`, `IPHONEOS_DEPLOYMENT_TARGET` and `SWIFT_VERSION` build settings in Xcode
4. **CI scripts** — `.github/workflows/*.yml`, `Fastfile`, `Makefile` — these reveal the canonical build/test invocation

Use the testing framework the repo already uses. Both XCTest and Swift Testing can coexist in one target — match what the file you're adding tests next to uses.

## Project Type Detection

| Indicator | Project type | Build tool |
|-----------|--------------|------------|
| `Package.swift` only | Swift Package Manager | `swift build` / `swift test` |
| `*.xcodeproj` or `*.xcworkspace` | Xcode project (often app/iOS) | `xcodebuild` |
| Both | SPM library + Xcode app shell | Use SPM for library targets, Xcode for app targets |
| `Project.swift` (Tuist) | Tuist-generated Xcode project | Run `tuist generate` first, then xcodebuild |
| `project.yml` (XcodeGen) | XcodeGen-generated project | Run `xcodegen generate` first |

If both an `.xcodeproj` and `.xcworkspace` exist (e.g. CocoaPods), **always pass `-workspace` not `-project`** to xcodebuild.

## Build Commands

### Swift Package Manager

| Scope | Command |
|-------|---------|
| Build all | `swift build` |
| Build a target | `swift build --target MyLibrary` |
| Build for release | `swift build -c release` |

### Xcode (`xcodebuild`)

```
xcodebuild build \
  -workspace MyApp.xcworkspace \
  -scheme MyAppScheme \
  -destination 'platform=iOS Simulator,name=iPhone 15' \
  -configuration Debug
```

- Always specify `-destination` for iOS/tvOS/watchOS — the default may not exist on the build machine
- Use `-quiet` to suppress xcodebuild's chatty output, and pipe to `xcbeautify`/`xcpretty` if installed
- For deterministic CI builds add `-derivedDataPath ./DerivedData`

## Test Commands

### Swift Package Manager

| Scope | Command |
|-------|---------|
| All tests | `swift test` |
| Filter by test name (XCTest) | `swift test --filter MyClassTests/testFooBar` |
| Filter by test name (Swift Testing) | `swift test --filter MyTestSuite.fooBar` |
| Parallel | `swift test --parallel` |
| Single platform | `swift test --triple x86_64-apple-macosx` (rare; usually skip) |

### Xcode

```
xcodebuild test \
  -workspace MyApp.xcworkspace \
  -scheme MyAppScheme \
  -destination 'platform=iOS Simulator,name=iPhone 15' \
  -only-testing:MyAppTests/MyClassTests/testFooBar
```

- `-only-testing:` and `-skip-testing:` accept `Bundle/Class/Method` paths and may be repeated
- `xcodebuild test-without-building` skips compilation if you've already built
- For Swift Testing in Xcode 16+, use the same `-only-testing:` syntax — the runner handles both frameworks

## Lint Command

Use the repo's lint tooling first:

- `swiftlint lint --quiet` (autocorrect: `swiftlint --fix`) when `.swiftlint.yml` is present
- `swiftformat .` when `.swiftformat` is present
- Some projects gate format on a build phase — running `xcodebuild` may already invoke it

## Project Layout

### SPM

```
Package.swift
Sources/
└── MyLibrary/
    ├── Foo.swift
    └── Bar.swift
Tests/
└── MyLibraryTests/
    └── FooTests.swift
```

- Test target name conventionally is `<TargetName>Tests` and lives in `Tests/<TargetName>Tests/`
- Test target must list its production target as a dependency in `Package.swift`:

  ```swift
  .testTarget(
      name: "MyLibraryTests",
      dependencies: ["MyLibrary"]),
  ```

### Xcode

- Tests live in a separate target (e.g. `MyAppTests`) added to the scheme's "Test" action
- The test target's "Host Application" determines whether tests run on the simulator with the app loaded (unit tests) or as a UI test runner

## Imports

- XCTest: `import XCTest` plus `@testable import MyLibrary` to access `internal` symbols
- Swift Testing: `import Testing` plus `@testable import MyLibrary`
- `@testable` works only when the production target is built with `-enable-testing` (the SPM test target and Xcode "Debug" config do this by default)
- Never mark production code `public` solely to make it visible to tests — use `@testable import` instead

## Test File Templates

### Swift Testing (Xcode 16 / Swift 6)

```swift
import Testing
@testable import MyLibrary

@Suite("Calculator")
struct CalculatorTests {
    @Test("add returns the sum of two integers")
    func addReturnsSum() {
        let calc = Calculator()
        #expect(calc.add(2, 3) == 5)
    }

    @Test("add throws on overflow", arguments: [
        (Int.max, 1),
        (Int.min, -1),
    ])
    func addThrowsOnOverflow(a: Int, b: Int) {
        #expect(throws: ArithmeticError.self) {
            try Calculator().add(a, b)
        }
    }
}
```

### XCTest

```swift
import XCTest
@testable import MyLibrary

final class CalculatorTests: XCTestCase {
    func testAddReturnsSum() {
        let calc = Calculator()
        XCTAssertEqual(calc.add(2, 3), 5)
    }

    func testAddThrowsOnOverflow() {
        XCTAssertThrowsError(try Calculator().add(.max, 1)) { error in
            XCTAssertEqual(error as? ArithmeticError, .overflow)
        }
    }
}
```

- XCTest requires test methods to start with `test` and take no arguments
- Mark XCTest classes `final` to silence warnings and prevent unintended subclassing
- Use `XCTUnwrap` instead of force-unwrapping (`!`) inside tests so the failure is reported rather than crashing the runner

## Async, Throws, and Concurrency

- Test methods may be `async` and/or `throws` in both frameworks
- For asynchronous expectations under XCTest, use `XCTestExpectation` + `wait(for:timeout:)` only when you cannot refactor to `async`
- For Swift Testing, use `await confirmation { ... }` to assert that a callback fires
- Cancel tasks deliberately with `Task.cancel()` instead of relying on test timeout

## Common Errors

| Error | Fix |
|-------|-----|
| `cannot find 'X' in scope` from a test | Add `@testable import MyLibrary` (and ensure the test target depends on it) |
| `module 'MyLibrary' was not compiled for testing` | Build the production target with `-enable-testing`; SPM test targets do this automatically — Xcode Debug configs need "Enable Testability" = YES |
| `failed to launch test runner` (Xcode) | Simulator destination may be invalid; list with `xcrun simctl list devices` and pick an existing one |
| `No such module 'XCTest'` outside a test target | XCTest is only available in test targets — do not import it from production code |
| `Static method 'expect(_:_:sourceLocation:)' is unavailable` / `No such module 'Testing'` | Swift Testing requires Swift 6 / Xcode 16+. On older toolchains, fall back to XCTest |
| `Symbol not found: _OBJC_CLASS_$_...` | Linker missing a framework; add it to the test target's "Link Binary With Libraries" |
| `signal SIGABRT` in tests | Often a force-unwrap on `nil`; replace `!` with `XCTUnwrap` to localize the failure |
| `MainActor-isolated property cannot be referenced from a non-isolated context` | Mark the test method `@MainActor` or move setup into a `MainActor` task |
| `Sandbox: ... deny file-write-create` | Use `FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)` instead of writing to fixed paths |
| Test discovery shows zero tests on Linux | XCTest on Linux needs `XCTMain([testCase(MyTests.allTests), ...])` in `Tests/LinuxMain.swift` (legacy SwiftPM only); for Swift 5.4+ this is auto-generated |

## Mocking Rules

Swift has no Mockito-equivalent — favor protocol-oriented design:

- Define a **protocol** for the dependency, pass it via initializer, and implement a fake/stub struct in the test target
- For URL/HTTP, use `URLProtocol` subclasses to intercept `URLSession` requests, or use `MockingbirdSwift` / `Cuckoo` if the repo already adopts them
- For dates/clocks, inject a `Clock` (`ContinuousClock`, `SuspendingClock`, or a custom `Clock`-conforming type) — do not call `Date()` directly in business logic
- Avoid `swizzling` and runtime hacks — they break under Swift's optimizer

If a test needs more than 3 mocks, flag it as a design smell.

## Cross-Platform Considerations

- Swift on Linux supports XCTest but **not** all of Foundation — guard with `#if canImport(Darwin)` or `#if os(macOS)` only when necessary
- Use `String(decoding:as:)` rather than `String(contentsOf:encoding:)` for cross-platform reads
- Be careful with `Bundle.main` in tests — on macOS unit tests it points to `xctest`, not your bundle; use `Bundle(for: type(of: self))` (XCTest) or a resource-bundle helper

## Dependency Installation (Last Resort)

Only add dependencies after investigation confirms they are missing.

`Package.swift`:

```swift
.package(url: "https://github.com/apple/swift-collections.git", from: "1.1.0"),
```

Then add to the test target's `dependencies:`. For CocoaPods/Carthage, edit `Podfile`/`Cartfile` and run `pod install` / `carthage update --use-xcframeworks`.

## Skip Coverage Tools

Do not configure or run coverage tools (`-enableCodeCoverage YES`, `xccov`, `slather`). Coverage is measured separately by the evaluation harness.
