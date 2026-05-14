# Rust Extension

Language-specific guidance for Rust test generation.

## Rule #1: Investigate the Repo First

Before writing any test or running any command, read:

1. **Existing tests** — look at `#[cfg(test)] mod tests` blocks inside `src/`, integration tests in `tests/`, doc tests in source comments, and any `examples/` that double as smoke tests
2. **`Cargo.toml`** — workspace layout (`[workspace]`), edition, `dev-dependencies`, feature flags, `[[bench]]` / `[[test]]` declarations
3. **`Cargo.lock`** — if checked in, you must not break it without intent
4. **Toolchain** — `rust-toolchain.toml` pins the channel (stable / nightly / specific version)
5. **`build.rs`** — custom build scripts may set `cfg` flags or generate code that tests rely on

Match the repo's existing conventions — assertion macros, mock approach, feature-gating — exactly. Do not introduce `tokio::test` if the repo uses `async-std`, etc.

## Toolchain Detection

| Indicator | Meaning |
|-----------|---------|
| `rust-toolchain.toml` with `channel = "..."` | Use rustup to install/select that channel — `rustup show active-toolchain` |
| `rust-version = "1.x"` in `Cargo.toml` | Minimum supported Rust version (MSRV); do not use newer language features |
| `[workspace]` in root `Cargo.toml` | Multi-crate workspace; commands accept `-p <crate>` to target one member |
| `nightly` channel | Tests may use `#![feature(...)]` flags; do not remove them |

## Build Commands

| Scope | Command |
|-------|---------|
| Type-check fast | `cargo check` |
| Type-check whole workspace | `cargo check --workspace --all-targets` |
| Build (debug) | `cargo build` |
| Build with all features | `cargo build --all-features` |
| Build a single crate | `cargo build -p crate-name` |
| Build tests without running | `cargo test --no-run` |

`cargo check` is far faster than `cargo build` and catches almost the same errors. Prefer it during the fix loop; use `cargo build --tests` (or `cargo test --no-run`) before declaring tests compilable.

## Test Commands

| Scope | Command |
|-------|---------|
| All tests | `cargo test` |
| Workspace | `cargo test --workspace` |
| Single crate | `cargo test -p crate-name` |
| Filter by name | `cargo test substring_of_test_name` |
| Exact name | `cargo test -- --exact path::to::test_fn` |
| Single integration file | `cargo test --test file_stem` (no `.rs`) |
| Doc tests only | `cargo test --doc` |
| Show stdout | `cargo test -- --nocapture` |
| Single-threaded | `cargo test -- --test-threads=1` |
| Ignored tests | `cargo test -- --ignored` |
| With features | `cargo test --features "feat1 feat2"` |
| All features | `cargo test --all-features` |

- Arguments before `--` are for cargo; arguments after `--` go to the test binary
- `cargo test foo` runs every test with `foo` in its full path (`module::tests::foo_does_a_thing`) — to avoid surprise matches use `--exact`
- `cargo nextest run` is significantly faster if the repo already uses it (`Cargo.toml` `[profile.nextest...]` or `.config/nextest.toml`) — match the repo's choice

## Lint Command

Use the repo's lint script first. Otherwise:

- `cargo fmt --all -- --check` (CI), `cargo fmt` (apply)
- `cargo clippy --all-targets --all-features -- -D warnings`
- If `clippy.toml` / `rustfmt.toml` exists, the project has opinions — never override them in your tests

## Project Layout

```
my_crate/
├── Cargo.toml
├── src/
│   ├── lib.rs        # library crate root
│   ├── main.rs       # binary crate root (mutually OK with lib.rs)
│   └── module.rs     # private/public module
├── tests/            # integration tests — each .rs is a separate crate
│   └── widget.rs
├── benches/          # cargo bench targets
└── examples/         # cargo run --example name
```

| Test type | Where | Sees |
|-----------|-------|------|
| Unit test | `#[cfg(test)] mod tests` inside the source file | Private items in the surrounding module |
| Integration test | `tests/<name>.rs` | Only the public API of the crate |
| Doc test | `///` doctests in source comments | Only the public API; runs via `cargo test --doc` |

- **Unit tests** at the bottom of `module.rs`:

  ```rust
  #[cfg(test)]
  mod tests {
      use super::*;

      #[test]
      fn name_scenario_expected() {
          // ...
      }
  }
  ```

- **Integration tests** import the crate by name: `use my_crate::PublicType;`
- Helpers shared between integration tests must live in `tests/common/mod.rs` (the `mod.rs` form prevents cargo from treating them as a top-level test crate)

## Test Function Patterns

| Kind | Attribute |
|------|-----------|
| Sync test | `#[test]` |
| Should panic | `#[test] #[should_panic(expected = "message substring")]` |
| Ignored (long/manual) | `#[test] #[ignore = "reason"]` |
| Async test (Tokio) | `#[tokio::test]` (or `#[tokio::test(flavor = "multi_thread")]`) |
| Async test (async-std) | `#[async_std::test]` |
| Returning `Result` | `fn name() -> Result<(), Box<dyn Error>>` — use `?` instead of `.unwrap()` |

Pick the async harness the repo already uses. Do not mix `tokio` and `async-std` in tests.

## Common Errors

| Error | Fix |
|-------|-----|
| `cannot find type X in this scope` | Add `use crate::module::X;` or `use super::*;` inside the test module |
| `function or associated item not found in 'X'` | Verify the method exists on the exact type; check trait imports (e.g. `use std::io::Read`) |
| `the trait bound 'X: Y' is not satisfied` | Either implement the trait, add a `where` bound, or change the test to use a type that already implements it |
| `borrow of moved value` | Add `.clone()`, borrow with `&`, or restructure ownership — do not use `mem::transmute` to dodge it |
| `cannot borrow as mutable` | Make the binding `let mut x` or restructure to avoid simultaneous mutable + immutable borrows |
| `lifetime may not live long enough` | Add explicit lifetime annotations or use owned types (`String` instead of `&str`) in the test |
| `mismatched types` between `i32` and `usize` | Use `as` casts deliberately or change the literal type with a suffix (`5usize`, `5u32`) |
| `unresolved import 'crate::...'` in `tests/foo.rs` | Integration tests must import via the **crate name** (as listed in `Cargo.toml`), not `crate::` |
| `error: no test target found` for `cargo test --test foo` | The file must live directly in `tests/`, not `tests/subdir/foo.rs` (subdirs are treated as helpers) |
| `attempt to subtract with overflow` (debug) | Underflow on unsigned types; use `checked_sub`/`saturating_sub` or compare before subtracting |
| Doctest fails to compile | Use a leading "# " on hidden setup lines; mark code blocks `ignore`/`no_run`/`should_panic` if needed |
| `the following imports are unused` (warning treated as error) | Remove unused `use` statements; do not silence with `#[allow(unused_imports)]` |

## Mocking Rules

Rust has no single dominant mocking framework. Match the repo:

- **Trait + struct fakes** (most idiomatic): define a trait, pass `Arc<dyn Trait>` or generic `T: Trait`, implement a fake struct in tests
- **`mockall`** crate: `#[automock]` on a trait generates `MockTrait` for use in tests
- **`mockito`** / **`wiremock`**: HTTP server mocks for client tests
- **`tempfile`**: scoped temp directories that auto-clean (`tempfile::tempdir()`)

Avoid `unsafe` patches to "mock" free functions. Refactor to inject a trait instead. If a test needs more than 3 mocks, flag it as a design smell.

## Features and `cfg`

- Tests behind a feature flag run only when that feature is enabled — use `#[cfg(feature = "foo")]` on the `mod tests` or individual `#[test]` functions
- `--all-features` exercises everything but may pull conflicting features in some workspaces; check `cargo test --all-features` is part of CI before relying on it
- Use `#[cfg(test)]` to gate test-only helpers in production source files — not `#[cfg(feature = "test")]`

## Concurrency, IO, and `unsafe`

- Tests run in parallel by default. If your tests share global state (env vars, current dir, statics), serialize them with the `serial_test` crate (if present) or move state into the test
- Never write to `/tmp` or the repo dir directly — use `tempfile::tempdir()` so cleanup is automatic
- Tests in `unsafe` code should also run under Miri (`cargo +nightly miri test`) if the repo's CI does

## Dependency Installation (Last Resort)

Only add dependencies after investigation confirms they are missing:

```toml
[dev-dependencies]
mockall = "0.12"
tokio = { version = "1", features = ["macros", "rt-multi-thread"] }
```

Or via cargo:

```
cargo add --dev mockall
cargo add --dev tokio --features macros,rt-multi-thread
```

Match the major version of any tokio/serde/etc. already pinned by the workspace.

## Skip Coverage Tools

Do not configure or run coverage tools (`cargo tarpaulin`, `cargo llvm-cov`, `grcov`). Coverage is measured separately by the evaluation harness.
