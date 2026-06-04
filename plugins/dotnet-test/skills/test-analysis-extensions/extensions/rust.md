# Rust Test Framework Reference (built-in `#[test]`, `cargo test`)

Reference data for analyzing Rust test code. Used by the polyglot test analysis skills.

## Capability tags

| Capability | Support |
|------------|---------|
| Test discovery | Strong — `#[test]` / `#[tokio::test]` / `#[cfg(test)] mod tests` / `tests/` integration directory |
| Assertion detection | Strong — `assert!`, `assert_eq!`, `assert_ne!`, `?` on `Result` tests |
| Sleep/delay detection | Strong — `thread::sleep`, `tokio::time::sleep` |
| Skip/ignore detection | Strong — `#[ignore]`, `#[cfg(...)]` gating |
| Setup/teardown detection | Moderate — no built-in fixtures; uses constructors and `Drop`, or external crates |
| Tag support | **report-only / convention-based** — no canonical attribute; some crates (`rstest`, `nextest`) support test filters by name |

## Test File Identification

| Convention | Description |
|------------|-------------|
| `#[test]` | Standard test attribute |
| `#[cfg(test)] mod tests { ... }` | Unit tests co-located with source |
| `tests/*.rs` | Integration tests (each file is a separate crate) |
| `#[tokio::test]` / `#[async_std::test]` | Async tests (need async runtime crate) |
| `#[rstest]` | Parametric tests via the `rstest` crate |
| `#[should_panic]` | Tests that expect a panic |
| Doc tests | `///` comments containing executable code blocks |
| `#[bench]` (nightly) / `criterion` benchmarks | Benchmarks |

## Assertion APIs

| Category | Built-in | proptest / quickcheck |
|----------|----------|-----------------------|
| Equality | `assert_eq!(actual, expected)` | (manual `prop_assert_eq!`) |
| Inequality | `assert_ne!(actual, expected)` | `prop_assert_ne!` |
| Boolean | `assert!(condition, "msg")` | `prop_assert!(...)` |
| Pattern match | `assert!(matches!(value, Pattern))` | n/a |
| Panic | `#[should_panic]` / `#[should_panic(expected = "msg")]` | n/a |
| Error | `result.unwrap()` (panics on error) / `?` propagation | n/a |
| Fail | `panic!("reason")` / `unreachable!()` | n/a |

Third-party libraries: `pretty_assertions` (`assert_eq!` with colored diffs), `assert_matches`, `claim` (`assert_ok!`, `assert_err!`).

**Result-returning tests** (Rust 2018+):
```rust
#[test]
fn parses_valid_input() -> Result<(), Box<dyn std::error::Error>> {
    let v = parse("1")?;
    assert_eq!(v, 1);
    Ok(())
}
```

## Sleep/Delay Patterns

| Pattern | Example |
|---------|---------|
| Sync sleep | `std::thread::sleep(Duration::from_secs(1))` |
| Async sleep (tokio) | `tokio::time::sleep(Duration::from_secs(1)).await` |
| async-std sleep | `async_std::task::sleep(Duration::from_secs(1)).await` |
| Spin wait | `while !cond() { std::thread::sleep(...) }` |
| Acceptable (tokio time control) | `tokio::time::pause()` + `tokio::time::advance(...)` |

## Skip/Ignore Annotations

| Mechanism | Example |
|-----------|---------|
| `#[ignore]` | Excluded by default; run with `cargo test -- --ignored` |
| `#[ignore = "reason"]` | With reason (Rust 1.55+) |
| `#[cfg(feature = "x")]` | Skip unless feature enabled |
| `#[cfg(target_os = "linux")]` | Skip on other OS |
| `#[cfg(not(miri))]` | Skip under Miri interpreter |
| Conditional skip | manual `if !cfg!(...) { return; }` (anti-pattern) |

## Exception Handling — Idiomatic Alternatives

```rust
// should_panic with specific message:
#[test]
#[should_panic(expected = "must be positive")]
fn parses_negative_panics() {
    parse_amount(-5);
}

// Result return + ?:
#[test]
fn places_order_ok() -> anyhow::Result<()> {
    let order = service.place_order(valid_order())?;
    assert_eq!(order.id, 42);
    Ok(())
}

// Match on Err for specific variant:
let err = service.place_order(empty).unwrap_err();
assert!(matches!(err, OrderError::Empty));
```

Flag tests that use `.unwrap()` on `Result` returns from production code without asserting the error variant — they conflate "unexpected error" with test failure.

## Mystery Guest — Common Rust Patterns

| Indicator | What to look for |
|-----------|------------------|
| File system | `std::fs::read`, `std::fs::write`, hard-coded paths |
| Database | `sqlx::PgPool::connect` against real DB, `rusqlite::Connection::open(path)` |
| Network | `reqwest::get`, `hyper` client to real URLs, raw `TcpStream::connect` |
| Environment | `std::env::var("X").unwrap()` |
| Acceptable | `tempfile::TempDir`, `httpmock`, `wiremock-rs`, `mockito`, `sqlx` test pool against in-memory SQLite, `tokio::test` with `start_paused = true` |

## Integration Test Markers

- `tests/` top-level directory contains integration tests
- Test names containing `_integration_`, `_e2e_`, `_acceptance_`
- Feature flags: `#[cfg(feature = "integration-tests")]`
- Crates like `testcontainers` imply integration
- `cargo nextest` profile names (`[profile.integration]`)

## Setup/Teardown

Rust has no native fixture framework. Common patterns:

| Pattern | Description |
|---------|-------------|
| Helper function | `fn setup() -> Foo { ... }` invoked at the start of each test |
| `Drop` implementation | Side-effect cleanup on test-local guard structs |
| `rstest` fixtures | `#[fixture] fn db() -> Db { ... }` + `#[rstest] fn t(db: Db) { ... }` |
| `test-context` crate | Per-test `setup` / `teardown` traits |
| `serial_test` crate | Avoid parallel test interference with `#[serial]` |
| `once_cell` / `lazy_static` | Lazy global init (use cautiously — shared state across tests) |

## Tag/Trait Attributes (for `test-tagging`)

**Default mode: report-only / convention-based.** Rust has no canonical per-test tag attribute. Strategies:

- **Module grouping**: `mod positive { ... }`, `mod boundary { ... }` — works with `cargo test boundary::`
- **Test name prefixes**: `fn test_negative_invalid_input_returns_error()` — filterable via `cargo test negative_`
- **Feature flags** for integration/E2E: `#[cfg(feature = "e2e")]`
- **`cargo nextest`** supports test groups via `nextest.toml` filtering expressions

Only switch to `auto-edit` mode when the project already follows one convention.

## Language-specific calibration notes

- **Doc tests** are real tests — `cargo test` runs them. Treat as tests if user includes lib doc comments in scope.
- **`#[should_panic]` without `expected = "..."`** passes on ANY panic — that's a smell (overly broad).
- **`.unwrap()` and `.expect()` in tests** are acceptable for type-correct unwrapping but obscure error sources. Recommend `?` on `Result`-returning tests where possible.
- **Property-based tests** (`proptest!`, `quickcheck!`) generate input cases; treat as parametrized tests, not duplicates.
- **`#[ignore]` without a reason** is a smell — flag as Ignored Test with low severity.
- **Async tests requiring `#[tokio::test]` but missing it** silently never run. Flag any `async fn` test missing the runtime attribute.
- **`thread::sleep` in tests** is a Sleepy Test; prefer `tokio::time::pause()` for async or explicit polling for sync.
- **Tests that mutate `static mut` or global `Mutex<...>` state** require `#[serial]` (from `serial_test`) — otherwise flaky under parallel `cargo test`.
- **`#[cfg(test)]` modules cross-compiled with `#![deny(warnings)]`** sometimes fail builds — note but don't flag as smell.
- **Bare `assert!(x)` with no message** in `assert_eq!`-suitable positions is acceptable; do not require messages.
